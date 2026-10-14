using System.Windows;
using Se7en.DualSense.App.Services;

namespace Se7en.DualSense.App.Pages;

/// <summary>Page qui reçoit l'état de la manette à chaque image.</summary>
public interface ILivePage
{
    void Update(in FrameData frame);
}

/// <summary>Services de la fenêtre principale accessibles aux pages.</summary>
public interface IShell
{
    void Navigate(string page);
    void ShowModal(UIElement content);
    void CloseModal();
    void Toast(string title, string message, NotifyKind kind = NotifyKind.Info);
    /// <summary>Pastille rouge sur l'onglet Sticks quand un drift est détecté.</summary>
    void SetDriftAlert(bool drift);
}
