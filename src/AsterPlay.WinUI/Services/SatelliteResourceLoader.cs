using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;

namespace AsterPlay.WinUI.Services;

/// <summary>
/// Resolves managed satellite assemblies from resources/{culture}/.
/// Native WinUI resources and PRI files retain their original SDK-required
/// locations; this resolver only handles managed *.resources.dll.
/// </summary>
internal static class SatelliteResourceLoader
{
    private static bool _initialized;

    internal static void Install()
    {
        if (_initialized)
            return;

        _initialized = true;
        AssemblyLoadContext.Default.Resolving += ResolveSatellite;
    }

    private static Assembly? ResolveSatellite(
        AssemblyLoadContext context,
        AssemblyName requested)
    {
        var name = requested.Name;
        var culture = requested.CultureName;

        if (string.IsNullOrEmpty(name) ||
            !name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(culture))
        {
            return null;
        }

        try
        {
            // Do not let culture or assembly names become arbitrary paths.
            if (!string.Equals(
                    CultureInfo.GetCultureInfo(culture).Name,
                    culture,
                    StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(name) != name)
            {
                return null;
            }

            var path = Path.Combine(
                AppContext.BaseDirectory,
                "resources",
                culture,
                name + ".dll");

            return File.Exists(path)
                ? context.LoadFromAssemblyPath(path)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (FileLoadException)
        {
            return null;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
