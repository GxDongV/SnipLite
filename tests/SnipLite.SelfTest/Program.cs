global using System.IO;

namespace SnipLite.SelfTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var command = args.FirstOrDefault() ?? "--selftest";
        switch (command)
        {
            case "--selftest": SelfTest.Run(); break;
            case "--menu-test": SelfTest.RunMenuCheck(); break;
            case "--target-test": SelfTest.RunTargetCheck(); break;
            case "--text-test": SelfTest.RunTextCheck(); break;
            case "--demo": SelfTest.RenderDemo(); break;
            case "--annotation-demo": SelfTest.RenderAnnotationDemo(); break;
            case "--preferences-demo": SelfTest.RenderPreferencesDemo(); break;
            case "--feedback-demo": SelfTest.RenderFeedbackDemo(); break;
            case "--inline-demo": SelfTest.RenderInlineDemo(); break;
            case "--recognition-demo": SelfTest.ShowRecognitionDemo(); break;
            case "--result-demo": SelfTest.RenderResultDemo(); break;
            default:
                Console.Error.WriteLine($"Unknown diagnostic command: {command}");
                return 2;
        }
        Console.WriteLine($"Diagnostics {(Environment.ExitCode == 0 ? "completed" : "failed")}. Results: {SelfTest.OutputDir}");
        return Environment.ExitCode;
    }
}
