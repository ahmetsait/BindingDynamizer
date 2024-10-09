
//          Copyright Ahmet Sait 2024.
// Distributed under the Boost Software License, Version 1.0.
//    (See accompanying file LICENSE_1_0.txt or copy at
//          https://www.boost.org/LICENSE_1_0.txt)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace BindingDynamizer;

internal static class Program
{
	private static readonly Assembly assembly = Assembly.GetExecutingAssembly();
	private static readonly AssemblyName assemblyName = assembly.GetName();
	private static readonly string assemblyLocation = assembly.Location;
	private static readonly FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assemblyLocation);
	private static readonly string? copyright = fileVersionInfo.LegalCopyright;
	private static readonly string exeName = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);
	private static readonly string? version = assemblyName.Version?.ToString(3);

	private static readonly string versionText = string.Join(" - ", string.Join(' ', fileVersionInfo.ProductName, version), copyright);

	private static readonly string helpText = versionText + "\n\n" + $"""
	Converts D language static bindings into BindBC compatible dynamic ones.
	Outputs dynamic loader code to standard output.
	
	Usage:
	    {exeName} [<option>...] <file|folder>...
	
	Options:
	    --recursive, -r
	        Convert files in folders recursively.
	
	    --output-dir <directory>
	              -o <directory>
	        Outputs converted bindings to the specified directory.
	        Files inside nested folders are kept in relative structure.
	
	    --search-prefix <prefix>
	                 -s <prefix>
	        Functions starting with this pattern only will be converted.
	        Example: 'FT_' for FreeType bindings,
	                 'gl' for OpenGL bindings,
	                 'hb_' for HarfBuzz bindings,
	                 'SDL_' for Simple DirectMedia Layer bindings...
	        Default: 'x_'
	
	    --static-version-string <version_string>
	                         -v <version_string>
	        Version string to be used inside version blocks for conditional compilation.
	        Example: 'BindFT_Static' for FreeType bindings.
	        Default: 'BindX_Static'
	
	    --output-postfix <postfix>
	                  -p <postfix>
	        Postfix string to be appended at the end of file names that are converted.
	        Default: '-converted'
	
	    --help, -?
	        Shows this help text.
	
	    --version
	        Shows version info.
	""";

	private static void Main(string[] args)
	{
		const string indent = "    "; // FIXME: Make this controllable via command line args
		const string loaderFormat = "lib.bindSymbol(cast(void**)&{0}, \"{0}\");";

		string fileGlob = "*.d"; // FIXME: Make this controllable via command line args
		string functionPointerPrefix = "fp_";
		SearchOption recursiveness = SearchOption.TopDirectoryOnly;
		string? outputDir = null;
		string outputPostfix = "-converted";
		string staticVersionString = "BindX_Static";

		// D language accepts all unicode letters so we use \p{}
		string identifierPattern = @"[_\p{L}][_\p{L}\p{N}]*";
		var identifierRegex = new Regex(identifierPattern);
		string identifierPostfixPattern = @"[_\p{L}\p{N}]+";
		//Regex identifierPostfixRegex = new Regex(identifierPostfixPattern);

		string functionPattern = "/\\*.*?\\*/|//[^\n]*?" + // We also capture comments to preserve them later
			$@"|module (?<module>{identifierPattern}(\.{identifierPattern})*);" + // Used in dynamic loader code generation
			"|(?<returnType>[^\n]*?) (?<name>x_" + identifierPostfixPattern + ") (?<parameters>\\(.*?\\));"; // Actual function signature

		char[] invalidFileChars = Path.GetInvalidFileNameChars();
		char[] invalidPathChars = Path.GetInvalidPathChars();

		var inputs = new List<string>(args.Length);
		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];
			if (arg.StartsWith('-'))
			{
				switch (arg)
				{
					case "--recursive":
					case "-r":
						recursiveness = SearchOption.AllDirectories;
						break;
					case "--output-dir":
					case "-o":
						i++;
						if (args.Length > i)
							if (!args[i].Any(c => invalidPathChars.Contains(c)))
								outputDir = args[i];
							else
								Console.WriteLine("Output directory argument contains illegal characters.");
						break;
					case "--search-prefix":
					case "-s":
						i++;
						if (args.Length > i)
							functionPattern = functionPattern.Replace("x_", Regex.Escape(args[i]));
						break;
					case "--static-version-string":
					case "-v":
						i++;
						if (args.Length > i)
							if (identifierRegex.IsMatch(args[i]))
								staticVersionString = args[i];
							else
								Console.WriteLine("Static version string argument is not a legal identifier.");
						break;
					case "--output-postfix":
					case "-p":
						i++;
						if (args.Length > i)
							if (!args[i].Any(c => invalidFileChars.Contains(c)))
								outputPostfix = args[i];
							else
								Console.WriteLine("Output postfix argument contains illegal characters.");
						break;
					case "--help":
					case "-?":
						Console.WriteLine(helpText);
						return;
					case "--version":
						Console.WriteLine(versionText);
						return;
					default:
						if (File.Exists(arg))
							inputs.Add(arg);
						else
						{
							Console.WriteLine("Unknown command line option: \"{0}\". Run \"{1} --help\" for help.", arg, exeName);
							return;
						}

						break;
				}
			}
			else
			{
				inputs.Add(arg); // Interpreted as file input
			}
		}

		if (inputs.Count == 0)
		{
			Console.WriteLine("No input. Run \"{0} --help\" for help.", exeName);
			return;
		}

		var loaderList = new List<string>(512);

		var functionRegex = new Regex(functionPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);
		string Replacer(Match match)
		{
			if (!string.IsNullOrEmpty(match.Groups["name"].Value))
			{
				StringBuilder result = new(128), dynamicPart = new(64);
				result.AppendFormat("version({0})\n{1}{2}\n", staticVersionString, indent, match.Value.Replace("\n", "\n" + indent));
				result.Append("else\n");
				result.Append('{');
				dynamicPart.AppendFormat("\nprivate alias {1}{2} = {3} function {4};",
					indent, functionPointerPrefix, match.Groups["name"], match.Groups["returnType"], match.Groups["parameters"]);
				dynamicPart.AppendFormat("\n__gshared {1}{2} {2};", indent, functionPointerPrefix, match.Groups["name"]);
				dynamicPart.Replace("\n", "\n" + indent);
				result.Append(dynamicPart);
				result.Append("\n}");
				loaderList.Add(string.Format(loaderFormat, match.Groups["name"].Value));
				return result.ToString();
			}
			else if (!string.IsNullOrEmpty(match.Groups["module"].Value))
			{
				loaderList.Add("\nimport " + match.Groups["module"].Value + ";");
			}

			return match.Value;
		}

		foreach (string input in inputs)
		{
			if (File.Exists(input))
			{
				Console.Error.Write("Converting \"{0}\"...", input);
				string code = File.ReadAllText(input, Encoding.UTF8);
				//RegexLog(functionRegex.Matches(code));
				string result = functionRegex.Replace(code, Replacer);
				string outPath = Path.Combine(outputDir ?? Path.GetDirectoryName(input) ?? ".", Path.GetFileNameWithoutExtension(input) + outputPostfix + Path.GetExtension(input));
				File.WriteAllText(outPath, result);
				//Console.WriteLine(result);
				Console.Error.WriteLine(" Done.");
			}
			else if (Directory.Exists(input))
			{
				string inputDir = Path.GetFullPath(input).TrimEnd(Path.DirectorySeparatorChar);
				foreach (string file in Directory.EnumerateFiles(inputDir, fileGlob, recursiveness).Select(Path.GetFullPath))
				{
					string relativeFilePath = Path.GetRelativePath(relativeTo: inputDir, file);
					string relativeFileDir = Path.GetDirectoryName(relativeFilePath)!;
					string outDir = Path.Combine(outputDir ?? Path.GetDirectoryName(file) ?? ".", relativeFileDir);
					string basename = Path.GetFileNameWithoutExtension(file);
					string outPath = Path.Combine(outDir, basename + outputPostfix + Path.GetExtension(file));
					Console.Error.Write("Converting \"{0}\" -> \"{1}\"...", file, outPath);
					string code = File.ReadAllText(file, Encoding.UTF8);
					//RegexLog(functionRegex.Matches(code));
					string result = functionRegex.Replace(code, Replacer);
					Directory.CreateDirectory(outDir);
					File.WriteAllText(outPath, result);
					//Console.WriteLine(result);
					Console.Error.WriteLine(" Done.");
				}
			}
		}

		loaderList.ForEach(Console.WriteLine); // Dynamic Loader Code
	}

	//private static void RegexLog(MatchCollection matches)
	//{
	//	for (int i = 0; i < matches.Count; i++)
	//	{
	//		Match match = matches[i];
	//		Console.Write("{" + i + "}: ");
	//		Console.WriteLine("\"" + match.Value + "\"");
	//		for (int j = 1; j < match.Groups.Count; j++)
	//		{
	//			Group group = match.Groups[j];
	//			Console.Write((group.Name + ": ").PadRight(12));
	//			Console.WriteLine("\"" + group.Value + "\"");
	//		}
	//
	//		Console.WriteLine();
	//	}
	//}
}
