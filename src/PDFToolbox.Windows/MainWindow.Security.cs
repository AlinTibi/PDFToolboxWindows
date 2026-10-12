using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Almarfeld.PdfEngine;
using Microsoft.Win32;

namespace Almarfeld.PdfToolbox;
public partial class MainWindow
{
    private readonly StackPanel securitySettings = new();
    private readonly PasswordBox securityPassword = new(), securityConfirm = new();
    private readonly TextBox visiblePassword = new() { Visibility = Visibility.Collapsed };
    private readonly CheckBox showPassword = new() { Content = "Show password" };
    private readonly TextBlock confirmationLabel = new() { Text = "Confirm password" };
    private readonly TextBlock passwordLabel = new() { Text = "PDF password" };
    private readonly TextBlock securityHelp = new();

    private void SetupSecurity()
    {
        var parent = (StackPanel)ToolHelp.Parent;
        parent.Children.Insert(parent.Children.IndexOf(ToolHelp) + 1, securitySettings);
        securitySettings.Children.Add(securityHelp);
        securitySettings.Children.Add(passwordLabel);
        AutomationProperties.SetName(securityPassword, "PDF password"); AutomationProperties.SetName(securityConfirm, "Confirm PDF password");
        AutomationProperties.SetName(visiblePassword, "Visible PDF password");
        securitySettings.Children.Add(securityPassword); securitySettings.Children.Add(visiblePassword);
        securitySettings.Children.Add(showPassword); securitySettings.Children.Add(confirmationLabel); securitySettings.Children.Add(securityConfirm);
        showPassword.Checked += (_, _) => { visiblePassword.Text = securityPassword.Password; securityPassword.Visibility = Visibility.Collapsed; visiblePassword.Visibility = Visibility.Visible; };
        showPassword.Unchecked += (_, _) => { securityPassword.Password = visiblePassword.Text; visiblePassword.Clear(); visiblePassword.Visibility = Visibility.Collapsed; securityPassword.Visibility = Visibility.Visible; };
        securitySettings.Visibility = Visibility.Collapsed;
    }
    private void ClearSecurityPasswords()
    {
        securityPassword.Clear(); securityConfirm.Clear(); visiblePassword.Clear(); showPassword.IsChecked = false;
    }
    private void SetSecurityTool(string value)
    {
        ClearSecurityPasswords(); var active = value is "Protect" or "Unlock" or "Compress";
        securitySettings.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        passwordLabel.Visibility = securityPassword.Visibility = showPassword.Visibility = value is "Protect" or "Unlock" ? Visibility.Visible : Visibility.Collapsed;
        securityConfirm.Visibility = confirmationLabel.Visibility = value == "Protect" ? Visibility.Visible : Visibility.Collapsed;
        securityHelp.Text = value switch
        {
            "Protect" => "AES-128 / PDF 1.6. Use 1–32 printable ASCII characters. The same password grants opening and owner access. This does not authenticate document authors.",
            "Unlock" => "Enter the correct password; restricted PDFs require the owner password. No cracking or bypass. Export an unencrypted copy.",
            _ => "Conservative lossless stream compression. No rasterization, image downsampling or JPEG recompression. Text, metadata, forms and annotations are retained. Already optimized files may not shrink."
        };
        if (active) { ToolHelp.Text = "Export a new copy. Sources are never changed. Passwords are not logged or persisted."; PagesList.Visibility = Visibility.Collapsed; }
    }
    private async Task ExportSecurity(FileItem file)
    {
        string? password = null, confirm = null;
        try
        {
            password = showPassword.IsChecked == true ? visiblePassword.Text : securityPassword.Password; confirm = securityConfirm.Password;
            if (tool == "Protect") PdfService.ValidateProtection(password, confirm);
            if (tool == "Unlock" && string.IsNullOrEmpty(password)) throw new PdfOperationException("Password", "Enter the correct PDF password.");
            var save = new SaveFileDialog { Filter = "PDF document|*.pdf", DefaultExt = ".pdf", AddExtension = true, OverwritePrompt = false,
                FileName = System.IO.Path.GetFileNameWithoutExtension(file.Path) + "-" + tool.ToLowerInvariant() + ".pdf" };
            if (save.ShowDialog(this) != true) return;
            var policy = (CollisionPolicy)new[] { 1, 0, 2 }[CollisionBox.SelectedIndex];
            if (policy == CollisionPolicy.Replace && System.IO.File.Exists(save.FileName) && MessageBox.Show(this,
                "Replace this existing OUTPUT PDF? Source files remain protected.", "Confirm output replacement", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            vm.Busy = true; operation = new(); vm.Progress = 0; vm.Results.Clear(); ResultsPanel.Visibility = Visibility.Visible;
            vm.Status = "Processing copy…";
            if (tool == "Compress")
            {
                var progress = new Progress<JobProgress>(p => { vm.Progress = 100d * p.Completed / Math.Max(1, p.Total); vm.Status = p.Stage; });
                var result = await engine.CompressAsync(file.Input, save.FileName, policy, progress, operation.Token);
                vm.Progress = 100;
                var message = result.Path == null ? $"Original: {result.OriginalBytes:N0} bytes; attempted output: {result.OutputBytes:N0} bytes. No reduction; no output saved."
                    : $"Original: {result.OriginalBytes:N0} bytes; output: {result.OutputBytes:N0} bytes; saved: {result.SavedBytes:N0} bytes ({result.SavedPercent:F2}%).";
                vm.Results.Add(new(result.Path ?? "No output saved", result.Path == null ? "Skipped" : "Success", message, result.OutputBytes)); vm.Status = message;
            }
            else
            {
                vm.Results.Add(tool == "Protect" ? await engine.ProtectAsync(file.Input, password, confirm!, save.FileName, policy, operation.Token)
                    : await engine.UnlockAsync(file.Input, password, save.FileName, policy, operation.Token));
                vm.Progress = 100; vm.Status = "Exported a validated copy. Source unchanged.";
            }
        }
        catch (Exception e) { Error(e); }
        finally { password = confirm = null; ClearSecurityPasswords(); vm.Busy = false; operation?.Dispose(); operation = null; }
    }
}
