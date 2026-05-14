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
        set => SetSelectedProfile(value);
    }

    public ObservableCollection<Profile> Profiles { get; } = new();

    public event Action? SelectedProfileChanged;

    public ProfileService(IProfileRepository profileRepo)
    {
        _profileRepo = profileRepo;
    }

    public async Task LoadProfilesAsync()
    {
        var selectedId = SelectedProfile?.Id;
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

        if (selectedId is null && Profiles.Count > 0)
        {
            SelectedProfile = Profiles[0];
        }
        else if (selectedId is not null)
        {
            var existing = Profiles.FirstOrDefault(p => p.Id == selectedId.Value);
            if (existing != null) SelectedProfile = existing;
            else SelectedProfile = Profiles.FirstOrDefault();
        }
    }

    public async Task UpdateProfileAsync(Profile profile)
    {
        await _profileRepo.UpdateAsync(profile);

        var idx = Profiles.ToList().FindIndex(p => p.Id == profile.Id);
        if (idx >= 0)
        {
            if (!ReferenceEquals(Profiles[idx], profile))
                Profiles[idx] = profile;
        }

        if (SelectedProfile?.Id == profile.Id || Profiles.Count == 1)
            SetSelectedProfile(idx >= 0 ? Profiles[idx] : profile, forceNotify: true);
    }

    private void SetSelectedProfile(Profile? profile, bool forceNotify = false)
    {
        if (!forceNotify && ReferenceEquals(_selectedProfile, profile))
            return;

        _selectedProfile = profile;
        SelectedProfileChanged?.Invoke();
    }
}
