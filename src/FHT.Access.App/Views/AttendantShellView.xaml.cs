using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FHT.Access.App.ViewModels;
using FHT.Access.Domain.Enums;

namespace FHT.Access.App.Views;

public partial class AttendantShellView : UserControl
{
    public AttendantShellView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
        DataContextChanged += (_, _) => SyncPasswordBox();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
        {
            PinBox.Clear();
            return;
        }

        FocusLoginIfNeeded();
    }

    private void FocusLoginIfNeeded()
    {
        if (DataContext is not AttendantShellViewModel { Screen: AccessUiState.AttendantLogin })
            return;

        PinBox.Clear();
        SyncPasswordBox();
        _ = UserBox.Focus();
    }

    private void PinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is AttendantShellViewModel vm && sender is PasswordBox box)
            vm.Pin = box.Password;
    }

    private void LoginField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (DataContext is AttendantShellViewModel vm && vm.LoginCommand.CanExecute(null))
            vm.LoginCommand.Execute(null);

        e.Handled = true;
    }

    private void SearchField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (DataContext is not AttendantShellViewModel vm)
            return;

        if (vm.Results.Count > 0)
            vm.PickSearchResultCommand.Execute(vm.Results[0]);
        else if (vm.SearchCommand.CanExecute(null))
            vm.SearchCommand.Execute(null);

        e.Handled = true;
    }

    private void PickSearchResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MemberSearchResult result }
            && DataContext is AttendantShellViewModel vm)
        {
            vm.PickSearchResultCommand.Execute(result);
        }
    }

    private void SelectMember_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AttendantShellViewModel vm || vm.PickedMember is null)
            return;

        vm.SelectMemberCommand.Execute(vm.PickedMember);
        e.Handled = true;
    }

    private void TogglePassword_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AttendantShellViewModel vm)
            return;

        if (!vm.IsPasswordVisible)
            PinBox.Password = vm.Pin ?? string.Empty;
    }

    private void SyncPasswordBox()
    {
        if (DataContext is AttendantShellViewModel vm)
            PinBox.Password = vm.Pin ?? string.Empty;
    }
}
