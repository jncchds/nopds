namespace Nopds.Tests;

internal static class TestFiles
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "TestData");

    public static string PathOf(string name) => Path.Combine(Dir, name);

    public static FileStream Open(string name) => File.OpenRead(PathOf(name));
}
