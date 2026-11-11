using System;
using System.Text.Json;
using System.Threading.Tasks;
using Lis.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Lis.Web.Services
{
    public class AuthSessionService
    {
        private readonly IAuthService _authService;
        private readonly IJSRuntime _js;
        private readonly NavigationManager _nav;
        private const string SessionStorageKey = "lis_auth_session";

        public event Action? OnAuthStateChanged;

        public UserSession? CurrentUser { get; private set; }
        public bool IsAuthenticated => CurrentUser != null;
        public bool IsInitialized { get; private set; } = false;

        public AuthSessionService(IAuthService authService, IJSRuntime js, NavigationManager nav)
        {
            _authService = authService;
            _js = js;
            _nav = nav;
        }

        public async Task InitializeAsync()
        {
            if (IsInitialized) return;

            try
            {
                var json = await _js.InvokeAsync<string?>("localStorage.getItem", SessionStorageKey);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    CurrentUser = JsonSerializer.Deserialize<UserSession>(json);
                }
            }
            catch
            {
                // In prerendering or unsupported storage, fallback to unauthenticated
                CurrentUser = null;
            }
            finally
            {
                IsInitialized = true;
                NotifyStateChanged();
            }
        }

        public async Task<(bool Success, string? ErrorMessage)> LoginAsync(string username, string password, bool rememberMe = true)
        {
            try
            {
                var session = await _authService.LoginAsync(username, password);
                if (session == null)
                {
                    return (false, "اسم المستخدم أو كلمة المرور غير صحيحة.");
                }

                CurrentUser = session;

                if (rememberMe)
                {
                    var json = JsonSerializer.Serialize(CurrentUser);
                    await _js.InvokeVoidAsync("localStorage.setItem", SessionStorageKey, json);
                }
                else
                {
                    var json = JsonSerializer.Serialize(CurrentUser);
                    await _js.InvokeVoidAsync("sessionStorage.setItem", SessionStorageKey, json);
                }

                // Audit log successful login
                await _authService.LogAuditAsync(session.Id, "LOGIN", "users", session.Id.ToString(), null, new { session.Username, Timestamp = DateTime.UtcNow });

                NotifyStateChanged();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"حدث خطأ أثناء تسجيل الدخول: {ex.Message}");
            }
        }

        public async Task LogoutAsync()
        {
            if (CurrentUser != null)
            {
                try
                {
                    await _authService.LogAuditAsync(CurrentUser.Id, "LOGOUT", "users", CurrentUser.Id.ToString(), null, new { CurrentUser.Username, Timestamp = DateTime.UtcNow });
                }
                catch { }
            }

            CurrentUser = null;

            try
            {
                await _js.InvokeVoidAsync("localStorage.removeItem", SessionStorageKey);
                await _js.InvokeVoidAsync("sessionStorage.removeItem", SessionStorageKey);
            }
            catch { }

            NotifyStateChanged();
            _nav.NavigateTo("login", forceLoad: true);
        }

        private void NotifyStateChanged()
        {
            OnAuthStateChanged?.Invoke();
        }
    }
}

