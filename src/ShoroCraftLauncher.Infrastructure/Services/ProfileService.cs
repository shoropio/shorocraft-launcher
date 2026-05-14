using System.Collections.ObjectModel;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class ProfileService : IProfileService
{
    private readonly IProfileRepository _profileRepo;
    private Profile? _selectedProfile;

    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (_selectedProfile != value)
            {
                _selectedProfile = value;
                SelectedProfileChanged?.Invoke();
            }
        }
    }

    public ObservableCollection<Profile> Profiles { get; } = new();

    public event Action? SelectedProfileChanged;

    public ProfileService(IProfileRepository profileRepo)
    {
        _profileRepo = profileRepo;
    }

    public async Task LoadProfilesAsync()
    {
        var profiles = await _profileRepo.GetAllAsync();
        Profiles.Clear();
        foreach (var p in profiles)
        {
            Profiles.Add(p);
        }

        if (Profiles.Count == 0)
        {
            var defaultProfile = new Profile
            {
                Name = "Vanilla",
                MinecraftVersion = "latest",
                Type = ShoroCraftLauncher.Core.Enums.ProfileType.Vanilla,
                MinRamMB = 2048,
                MaxRamMB = 4096,
                WindowWidth = 854,
                WindowHeight = 480
            };
            await _profileRepo.CreateAsync(defaultProfile);
            Profiles.Add(defaultProfile);
        }

        if (SelectedProfile == null && Profiles.Count > 0)
        {
            SelectedProfile = Profiles[0];
        }
        else if (SelectedProfile != null)
        {
            var existing = Profiles.FirstOrDefault(p => p.Id == SelectedProfile.Id);
            if (existing != null) SelectedProfile = existing;
            else SelectedProfile = Profiles.FirstOrDefault();
        }
    }

    public async Task UpdateProfileAsync(Profile profile)
    {
        await _profileRepo.UpdateAsync(profile);
        var idx = Profiles.IndexOf(profile);
        if (idx >= 0)
        {
            Profiles.RemoveAt(idx);
            Profiles.Insert(idx, profile);
        }
        if (SelectedProfile?.Id == profile.Id)
            SelectedProfile = profile;
    }
}
