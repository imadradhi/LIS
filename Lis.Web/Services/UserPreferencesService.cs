using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Lis.Web.Services
{
    public class UserPreferences
    {
        public string Theme { get; set; } = "dark"; // "dark" or "light"
        public string Density { get; set; } = "standard"; // "standard" or "compact"
        public string AccentColor { get; set; } = "cyan"; // "cyan", "blue", "emerald", "purple"
        public string Language { get; set; } = "ar"; // "ar" or "en"
        public string DefaultLandingPage { get; set; } = "/";
        public bool SoundEnabled { get; set; } = true;
        public bool IsSidebarCollapsed { get; set; } = false;

        public string GetAccentHex() => AccentColor switch
        {
            "blue" => "#3B82F6",
            "emerald" => "#10B981",
            "purple" => "#8B5CF6",
            _ => "#00B4D8" // cyan
        };
    }

    public class UserPreferencesService
    {
        private readonly IJSRuntime _js;
        private readonly NavigationManager? _nav;
        private const string StorageKey = "lis_user_preferences";

        public event Action? OnChange;
        public UserPreferences Current { get; private set; } = new();
        public bool IsLoaded { get; private set; } = false;

        public bool IsEnglish => Current.Language?.Equals("en", StringComparison.OrdinalIgnoreCase) == true;
        public bool IsArabic => !IsEnglish;
        public string Language => Current.Language;

        public string T(string ar, string en) => IsEnglish ? en : ar;

        public UserPreferencesService(IJSRuntime js, NavigationManager? nav = null)
        {
            _js = js;
            _nav = nav;
            if (_nav != null)
            {
                try
                {
                    var uri = _nav.ToAbsoluteUri(_nav.Uri);
                    if (Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query).TryGetValue("lang", out var langVal))
                    {
                        var langStr = langVal.ToString().ToLowerInvariant();
                        if (langStr == "ar" || langStr == "en")
                        {
                            Current.Language = langStr;
                        }
                    }

                    if (Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query).TryGetValue("theme", out var themeVal))
                    {
                        var themeStr = themeVal.ToString().ToLowerInvariant();
                        if (themeStr == "light" || themeStr == "dark")
                        {
                            Current.Theme = themeStr;
                        }
                    }
                }
                catch { }
            }
        }

        public async Task LoadAsync()
        {
            try
            {
                var json = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
                if (!string.IsNullOrEmpty(json))
                {
                    Current = JsonSerializer.Deserialize<UserPreferences>(json) ?? new();
                }

                if (_nav != null)
                {
                    var uri = _nav.ToAbsoluteUri(_nav.Uri);
                    if (Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query).TryGetValue("theme", out var themeVal))
                    {
                        var themeStr = themeVal.ToString().ToLowerInvariant();
                        if (themeStr == "light" || themeStr == "dark")
                        {
                            Current.Theme = themeStr;
                        }
                    }

                    if (Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query).TryGetValue("lang", out var langVal))
                    {
                        var langStr = langVal.ToString().ToLowerInvariant();
                        if (langStr == "ar" || langStr == "en")
                        {
                            Current.Language = langStr;
                        }
                    }
                }

                IsLoaded = true;
                await ApplyToDomAsync();
            }
            catch
            {
                Current = new();
                IsLoaded = true;
            }
            NotifyStateChanged();
        }

        public async Task SaveAsync(UserPreferences prefs)
        {
            Current = prefs;
            try
            {
                var json = JsonSerializer.Serialize(Current);
                await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
                await ApplyToDomAsync();
            }
            catch { }
            NotifyStateChanged();
        }

        public async Task ToggleThemeAsync()
        {
            Current.Theme = Current.Theme == "dark" ? "light" : "dark";
            await SaveAsync(Current);
        }

        public async Task ToggleLanguageAsync()
        {
            Current.Language = IsEnglish ? "ar" : "en";
            await SaveAsync(Current);
        }

        public async Task SetLanguageAsync(string lang)
        {
            Current.Language = lang;
            await SaveAsync(Current);
        }

        public async Task ToggleSidebarAsync()
        {
            Current.IsSidebarCollapsed = !Current.IsSidebarCollapsed;
            await SaveAsync(Current);
        }

        public async Task ApplyToDomAsync()
        {
            try
            {
                await _js.InvokeVoidAsync("lisApplyPreferences", Current.Theme, Current.Density, Current.GetAccentHex(), Current.Language);
            }
            catch { }
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}

