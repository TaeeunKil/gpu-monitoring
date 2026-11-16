namespace GpuMonitoring.Windows;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var smoke = args.Length >= 2 && args[0] == "--smoke-test" ? args[1] : null;
        var dataPath = smoke is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GpuMonitoring.Windows") : Path.Combine(smoke, "data");
        Directory.CreateDirectory(dataPath);
        try
        {
            using var form = new MainForm(dataPath, smoke);
            Application.Run(form);
            Environment.ExitCode = form.ResultCode;
        }
        catch (Exception e)
        {
            if (smoke is not null)
            {
                Directory.CreateDirectory(smoke);
                File.WriteAllText(Path.Combine(smoke, "failure.txt"), e.ToString());
                Environment.ExitCode = 1;
            }
            else
                MessageBox.Show("실행하지 못했습니다.\n" + e.Message, "GPU Monitoring", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
