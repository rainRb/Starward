using Microsoft.UI.Xaml;
using Starward.Core;
using Starward.Core.Steam;
using Starward.Helpers;
using System;
using System.IO;
using System.Net.Http;
using Windows.ApplicationModel.DataTransfer;

namespace Starward.Features.GameLauncher;

public sealed partial class GameLauncherSettingDialog
{
    private SteamMetadata? _steamMetadata;

    private void InitializeSteam()
    {
        NavigationItem_Steam.Visibility = CurrentGameBiz.Value == GameBiz.nap_cn ? Visibility.Visible : Visibility.Collapsed;
        if (CurrentGameBiz.Value != GameBiz.nap_cn) return;
        try
        {
            ComboBox_SteamLibrary.ItemsSource = SteamCnService.Libraries();
            ComboBox_SteamLibrary.SelectedIndex = 0;
            Toggle_SteamCN.IsOn = SteamCnService.Enabled;
            TextBox_SteamCommand.Text = SteamBridge.LaunchOptions;
        }
        catch (Exception ex) { TextBlock_SteamStatus.Text = ex.Message; }
    }

    private async void Button_SteamMetadata_Click(object sender, RoutedEventArgs e)
    {
        Button_SteamMetadata.IsEnabled = false;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
            SetSteamMetadata(await client.GetStringAsync("https://api.steamcmd.net/v1/info/" + SteamMetadata.AppId));
        }
        catch (Exception ex) { _steamMetadata = null; TextBlock_SteamStatus.Text = ex.Message; }
        finally { Button_SteamMetadata.IsEnabled = true; }
    }

    private async void Button_SteamImport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? file = await FileDialogHelper.PickSingleFileAsync(this.XamlRoot);
            if (File.Exists(file))
            {
                if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("Metadata file is too large.");
                SetSteamMetadata(await File.ReadAllTextAsync(file));
            }
        }
        catch (Exception ex) { _steamMetadata = null; TextBlock_SteamStatus.Text = ex.Message; }
    }

    private void SetSteamMetadata(string json)
    {
        _steamMetadata = SteamMetadata.Parse(json);
        TextBlock_SteamStatus.Text = $"AppID {SteamMetadata.AppId} · Build {_steamMetadata.BuildId}\n{_steamMetadata.InstallDirectory} / {_steamMetadata.Executable}\n{_steamMetadata.Depots.Count} public Windows depot(s)";
    }

    private async void Button_SteamApply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SteamCnService.IsBusy()) throw new InvalidOperationException("Exit Steam, ZZZ and the Steam helper before applying setup.");
            if (_steamMetadata is null) throw new InvalidOperationException(Lang.SteamCN_GetMetadataFirst);
            if (ComboBox_SteamLibrary.SelectedItem is not string library) throw new InvalidOperationException("Choose a Steam library.");
            string native = Path.Combine(GameLauncherService.GetGameInstallPath(CurrentGameId) ?? "", await _gameLauncherService.GetGameExeNameAsync(CurrentGameId));
            SteamCnService.SaveLaunch(native);
            SteamCnService.DeployHelper();
            SteamCnService.Manager().Apply(library, _steamMetadata, native);
            TextBox_SteamCommand.Text = SteamBridge.LaunchOptions;
            TextBlock_SteamStatus.Text = Lang.SteamCN_Applied;
        }
        catch (Exception ex) { TextBlock_SteamStatus.Text = ex.Message; }
    }

    private void Button_SteamRemove_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SteamCnService.Manager().Remove();
            Toggle_SteamCN.IsOn = false;
            SteamCnService.Enabled = false;
            TextBlock_SteamStatus.Text = Lang.SteamCN_Removed;
        }
        catch (Exception ex) { TextBlock_SteamStatus.Text = ex.Message; }
    }

    private void Toggle_SteamCN_Toggled(object sender, RoutedEventArgs e)
    {
        if (CurrentGameBiz.Value == GameBiz.nap_cn) SteamCnService.Enabled = Toggle_SteamCN.IsOn;
    }

    private void Button_SteamCopy_Click(object sender, RoutedEventArgs e)
    {
        var data = new DataPackage();
        data.SetText(SteamBridge.LaunchOptions);
        Clipboard.SetContent(data);
    }
}
