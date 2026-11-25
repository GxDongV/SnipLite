using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace SnipLite;

/// <summary>Configures Windows sign-in startup and the restart requested from preferences.</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "SnipLite";
    private const string TaskName = "SnipLite.Startup";
    internal const string RestartAsAdministratorArgument = "--restart-as-administrator";

    internal enum AdministratorRestartResult
    {
        RestartStarted,
        StartupFailed,
        RestartFailed
    }

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(Name) is string value && value.Length > 0 || HasAdminTask();
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Apply(bool enabled)
    {
        TryApply(enabled, false);
    }

    public static bool TryApply(bool enabled, bool administrator)
    {
        try
        {
            if (enabled && administrator || HasAdminTask())
            {
                if (RunElevatedHelper(enabled && administrator ? "--startup-admin:on" : "--startup-admin:off") != 0)
                    return false;
            }
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return false;

            if (enabled && !administrator)
                key.SetValue(Name, $"\"{Application.ExecutablePath}\"", RegistryValueKind.String);
            else
                key.DeleteValue(Name, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write("无法修改开机启动项", ex);
            return false;
        }
    }

    internal static AdministratorRestartResult EnableAdministratorStartupAndRestart()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User is not { } user) return AdministratorRestartResult.StartupFailed;
            return RunElevatedHelper(RestartAsAdministratorArgument, user.Value) switch
            {
                (int)AdministratorRestartResult.RestartStarted => AdministratorRestartResult.RestartStarted,
                (int)AdministratorRestartResult.RestartFailed => AdministratorRestartResult.RestartFailed,
                _ => AdministratorRestartResult.StartupFailed
            };
        }
        catch (Exception ex)
        {
            AppLog.Write("无法启用管理员开机启动", ex);
            return AdministratorRestartResult.StartupFailed;
        }
    }

    private static int RunElevatedHelper(params string[] arguments)
    {
        var start = new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true, Verb = "runas" };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var helper = Process.Start(start);
        if (helper is null) return 1;
        helper.WaitForExit();
        return helper.ExitCode;
    }

    internal static AdministratorRestartResult ConfigureAdministratorStartupAndLaunch(string userSid)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value != userSid ||
            !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) ||
            ConfigureAdminTask(true) != 0)
            return AdministratorRestartResult.StartupFailed;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(Name, throwOnMissingValue: false);
        }
        catch (Exception ex) { AppLog.Write("无法移除普通开机启动项", ex); }

        try
        {
            // A direct child inherits the helper's administrator token. The mutex keeps
            // it waiting until the old instance has saved its options and shut down.
            using var replacement = Process.Start(new ProcessStartInfo(Application.ExecutablePath)
            {
                Arguments = "--wait-for-instance --prefs:0",
                UseShellExecute = false
            });
            if (replacement is not null) return AdministratorRestartResult.RestartStarted;
        }
        catch (Exception ex) { AppLog.Write("无法以管理员身份重新启动", ex); }
        return AdministratorRestartResult.RestartFailed;
    }

    private static bool HasAdminTask()
    {
        object? service = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
            dynamic scheduler = service!; scheduler.Connect();
            dynamic task = scheduler.GetFolder("\\").GetTask(TaskName);
            return task is not null;
        }
        catch { return false; }
        finally { if (service is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(service); }
    }

    public static int ConfigureAdminTask(bool enabled)
    {
        object? service = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
            dynamic scheduler = service!; scheduler.Connect();
            dynamic folder = scheduler.GetFolder("\\");
            if (!enabled) { folder.DeleteTask(TaskName, 0); return 0; }
            dynamic definition = scheduler.NewTask(0);
            definition.RegistrationInfo.Description = UiText.T("在当前用户登录时启动 SnipLite");
            definition.Principal.UserId = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            definition.Principal.LogonType = 3; // InteractiveToken, no password stored.
            definition.Principal.RunLevel = 1; // HighestAvailable; registration is explicitly elevated.
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.ExecutionTimeLimit = "PT0S";
            dynamic trigger = definition.Triggers.Create(9); // Logon.
            trigger.UserId = definition.Principal.UserId;
            dynamic action = definition.Actions.Create(0); action.Path = Application.ExecutablePath;
            folder.RegisterTaskDefinition(TaskName, definition, 6, null, null, 3, null);
            return 0;
        }
        catch { return 1; }
        finally { if (service is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(service); }
    }
}
