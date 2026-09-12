using System.Windows;

namespace NovaVox.App;

/// <summary>Petite boîte de dialogue "saisir un texte" (nom de profil...), WPF n'en fournit pas nativement.</summary>
public partial class InputDialog : Window
{
    public string InputText { get; private set; } = "";

    public InputDialog(string prompt, string initialValue = "")
    {
        InitializeComponent();
        PromptText.Text = prompt;
        InputBox.Text = initialValue;
        InputBox.SelectAll();
        Loaded += (_, _) => InputBox.Focus();
    }

    public static string? Show(Window owner, string prompt, string initialValue = "")
    {
        var dialog = new InputDialog(prompt, initialValue) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.InputText : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        InputText = InputBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
