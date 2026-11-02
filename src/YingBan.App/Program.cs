namespace YingBan.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YingBan");
        int dataArg = Array.IndexOf(args, "--data-dir");
        if (dataArg >= 0 && args.Length > dataArg + 1) dataDirectory = Path.GetFullPath(args[dataArg + 1]);
        Directory.CreateDirectory(dataDirectory);
        using var mutex = new Mutex(true, "Local\\YingBan-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dataDirectory))).Substring(0, 16), out bool first);
        if (!first) { MessageBox.Show("陪肝小助手已经运行。请从任务栏托盘恢复窗口，或按 Ctrl+Alt+R。", "陪肝小助手"); return; }
        Application.ThreadException += (_, e) => Report(e.Exception, dataDirectory);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Report(ex, dataDirectory); };
        try { Application.Run(new MainForm(new SettingsStore(dataDirectory), args)); }
        catch (Exception e) { Report(e, dataDirectory); }
    }
    private static void Report(Exception e, string folder)
    {
        try { File.AppendAllText(Path.Combine(folder, "error.log"), $"{DateTime.UtcNow:o} {e}\n"); } catch { }
        MessageBox.Show("遇到错误：" + e.Message + "\n详细记录保存在应用数据目录的 error.log。", "陪肝小助手", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
