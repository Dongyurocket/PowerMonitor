namespace PowerMonitor;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--dump"))
        {
            using var mon = new PowerReader();
            mon.Update();
            Thread.Sleep(1000);
            mon.Update();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "sensors.txt"), mon.Dump());
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
