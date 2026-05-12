using System.Collections.ObjectModel;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IProfileService
{
    Profile? SelectedProfile { get; set; }
    ObservableCollection<Profile> Profiles { get; }
    event Action? SelectedProfileChanged;
    Task LoadProfilesAsync();
}
