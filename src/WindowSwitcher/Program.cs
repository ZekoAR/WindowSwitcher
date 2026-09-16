namespace WindowSwitcher;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--dump")
            return Dump.Run(args[1]);

        return App.Run();
    }
}
