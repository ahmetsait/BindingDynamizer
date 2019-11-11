Binding Dynamizer
=================
###### A command line tool for converting D language static bindings into BindBC compatible dynamic ones

Binding Dynamizer is a small helpful utility program that converts static bindings into dynamic BindBC style ones. It's a C# program that targets _.Net Core 3.0_ and works by doing a regex search-replace, so it's not as magical as you might think and the results require manual labor  although its usefulness is undeniable.

The tool can be improved by rewriting it in D and using libraries such as [libdparse](https://github.com/dlang-community/libdparse) or [pegged](https://github.com/PhilippeSigaud/Pegged) but I wanted something quick and dirty to get the job done.

## Usage
```
binding-dynamizer [options...] <files|folders>...

Options:
--recursive, -r
    Convert files in folders recursively.
    Note: Nested folders in inputs are not handled.

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
    Default: '_converted'

--help, -h
    Shows this help text.

--version
    Shows version info.
```
