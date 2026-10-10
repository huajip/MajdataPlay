using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO;
using MajdataPlay.Net;
using MajdataPlay.Utils;
using QRCoder;
using System;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static QRCoder.QRCodeGenerator;
#nullable enable
namespace MajdataPlay.Scenes.Login
{
    internal class LoginManager : MonoBehaviour
    {
        [SerializeField]
        TextMeshProUGUI _sceneTitle;
        [SerializeField]
        GameObject _usernameComponent;
        [SerializeField]
        GameObject _passwordComponent;
        [SerializeField]
        GameObject _qrCodeComponent;
        [SerializeField]
        GameObject _qrCodeLoading;
        [SerializeField]
        GameObject _qrCodeErrorIcon;

        [SerializeField]
        TMP_InputField _usernameInput;
        [SerializeField]
        TMP_InputField _passwordInput;
        [SerializeField]
        GameObject _usernameClearButton;
        [SerializeField]
        GameObject _passwordClearButton;
        [SerializeField]
        GameObject _button4;

        [SerializeField]
        GameObject _loading;
        [SerializeField]
        GameObject _hintTextObject;
        [SerializeField]
        TextMeshProUGUI _hintText;
        [SerializeField]
        Color ErrorColor;
        [SerializeField]
        Color SucceedColor;

        RawImage _qrCodeRawImage = null!;
        ApiEndpoint[] _apiEndpoints = Array.Empty<ApiEndpoint>();
        ApiEndpoint[] _enabledEndpoints = Array.Empty<ApiEndpoint>();

        sealed class AuthAttempt
        {
            public readonly ApiEndpoint Endpoint;
            public readonly CancellationTokenSource Cancellation = new();
            public readonly CancellationToken Token;
            public string RequestId = string.Empty;
            public bool Retired;
            public AuthAttempt(ApiEndpoint endpoint) { Endpoint = endpoint; Token = Cancellation.Token; }
        }
        readonly object _authGate = new();
        readonly CancellationTokenSource _sceneCancellation = new();
        AuthAttempt? _authAttempt;
        bool _lifecycleClosed;

        AuthAttempt BeginAuthAttempt(ApiEndpoint endpoint)
        {
            var attempt = new AuthAttempt(endpoint);
            lock (_authGate) _authAttempt = attempt;
            return attempt;
        }
        bool IsCurrent(AuthAttempt attempt)
        {
            lock (_authGate) return !_lifecycleClosed && !attempt.Retired && ReferenceEquals(_authAttempt, attempt);
        }
        void RetireAuthAttempt(AuthAttempt? attempt, bool accepted = false)
        {
            if (attempt is null) return;
            string id;
            lock (_authGate)
            {
                if (attempt.Retired) return;
                attempt.Retired = true;
                if (ReferenceEquals(_authAttempt, attempt)) _authAttempt = null;
                id = accepted ? string.Empty : attempt.RequestId;
            }
            attempt.Cancellation.Cancel();
            attempt.Cancellation.Dispose();
            if (!string.IsNullOrEmpty(id)) RevokeRetiredAttemptAsync(attempt.Endpoint, id);
        }
        async void RevokeRetiredAttemptAsync(ApiEndpoint endpoint, string id)
        {
            try
            {
                using var timeout = new CancellationTokenSource(10000);
                await RevokeAuthSession(endpoint, id, timeout.Token);
            }
            catch { MajDebug.LogError("Authorization session cleanup did not complete; server expiry remains the fallback"); }
        }
        async ValueTask<(string, AuthRequestResponse)> RegistryOwnedAuthSession(AuthAttempt attempt)
        {
            var response = await RegistryAuthSession(attempt.Endpoint, attempt.Token);
            bool retired;
            lock (_authGate)
            {
                retired = _lifecycleClosed || attempt.Retired || !ReferenceEquals(_authAttempt, attempt);
                if (!retired) attempt.RequestId = response.Item2.RequestId;
            }
            // A request can complete after transport cancellation. Revoke the
            // newly returned ticket instead of abandoning a live server QR.
            if (retired) RevokeRetiredAttemptAsync(attempt.Endpoint, response.Item2.RequestId);
            return response;
        }

        QrNfcPublisher? _nfcPublisher;
        long _nfcCheckGeneration = -1;
        bool _isReady = false;
        bool _isExited = false;
        readonly static QRCodeGenerator _qrGenerator = new ();
        readonly static Exception _exception = new();

        void Awake()
        {
            _nfcPublisher = QrNfcPublisher.CreateConfigured();
            if (_nfcPublisher != null && (MajEnv.Settings?.NFC?.Diagnostics ?? true))
                _nfcPublisher.Diagnostic = code => MajDebug.LogInfo("[MajNfc] " + code);
            _apiEndpoints = MajEnv.ApiEndpoints;
            _qrCodeRawImage = _qrCodeComponent.GetComponent<RawImage>();
            using var rentedApiEndpoints = new PooledList<ApiEndpoint>();
            for (var i = 0; i < _apiEndpoints.Length; i++)
            {
                var endpoint = _apiEndpoints[i];

                rentedApiEndpoints.Add(endpoint);
            }
            _enabledEndpoints = rentedApiEndpoints.ToArray();
            if (_enabledEndpoints.Length == 0)
            {
                MajInstances.SceneSwitcher.SwitchScene("List", false);
                return;
            }
            _loading.SetActive(false);
            Hint();
            _usernameInput.onValueChanged.AddListener(_ => UpdateClearButtons());
            _passwordInput.onValueChanged.AddListener(_ => UpdateClearButtons());
            UpdateClearButtons();
            LoginProcessor().Forget();
        }
        void OnDisable()
        {
            _nfcPublisher?.DisposeAndFlush();
            _nfcPublisher = null;
            if (_qrCodeRawImage != null) _qrCodeRawImage.texture = null!;
            if (!_lifecycleClosed)
            {
                _lifecycleClosed = true;
                _sceneCancellation.Cancel();
                _sceneCancellation.Dispose();
            }
            _isExited = true;
            _isReady = false;
            RetireAuthAttempt(_authAttempt);
        }
        void OnDestroy() => OnDisable();
        void OnApplicationQuit() => OnDisable();

        void Update()
        {
            _nfcPublisher?.Tick();
            if(!_isReady || _isExited)
            {
                return;
            }
            var isUsernameInputClicked = InputManager.IsSensorPressedInThisFrame(SensorArea.B2) ||
                                         InputManager.IsSensorPressedInThisFrame(SensorArea.C) ||
                                         InputManager.IsSensorPressedInThisFrame(SensorArea.E3);
            var isPasswordInputClicked = InputManager.IsSensorPressedInThisFrame(SensorArea.B3) ||
                                         InputManager.IsSensorPressedInThisFrame(SensorArea.B4) ||
                                         InputManager.IsSensorPressedInThisFrame(SensorArea.E4);
            var isUsernameClearBtnClicked = InputManager.IsSensorPressedInThisFrame(SensorArea.D3);
            var isPasswordClearBtnClicked = InputManager.IsSensorPressedInThisFrame(SensorArea.A3);
            if(isUsernameInputClicked)
            {
                Focus(_usernameInput);
            }
            if(isUsernameClearBtnClicked)
            {
                Focus(_usernameInput);
                _usernameInput.text = string.Empty;
            }
            if (isPasswordInputClicked)
            {
                Focus(_passwordInput);
            }
            if (isPasswordClearBtnClicked)
            {
                Focus(_passwordInput);
                _passwordInput.text = string.Empty;
            }
        }

        void UpdateClearButtons()
        {
            _usernameClearButton.SetActive(!string.IsNullOrEmpty(_usernameInput.text));
            _passwordClearButton.SetActive(!string.IsNullOrEmpty(_passwordInput.text));
        }

        static void Focus(TMP_InputField inputField)
        {
            inputField.Select();
            inputField.ActivateInputField();
            inputField.MoveTextEnd(false);
        }

        async UniTaskVoid LoginProcessor()
        {
            const int AUTH_FLAG_REQUESTING = 0;
            const int AUTH_FLAG_WAIT_FOR_PERMIT = 1;
            const int AUTH_FLAG_ERROR = 2;

            var sceneSwitcher = MajInstances.SceneSwitcher;
            for (var i = 0; i < _enabledEndpoints.Length; i++)
            {
                if (_lifecycleClosed) return;
                _nfcPublisher?.Clear();
                var endpoint = _enabledEndpoints[i];
                _loading.SetActive(false);
                Hint();
                var siteName = endpoint.Name;
                if(string.IsNullOrEmpty(siteName))
                {
                    siteName = endpoint.Url.Host;
                }
                _sceneTitle.text = $"Login to {siteName}";

                _qrCodeComponent.SetActive(true);
                _qrCodeLoading.SetActive(true);
                _usernameComponent.SetActive(true);
                _passwordComponent.SetActive(true);

                _qrCodeRawImage.texture = null!;
                _qrCodeRawImage.color = new Color(0.5f, 0.5f, 0.5f);
                _usernameInput.text = endpoint.RuntimeConfig.AuthUsername ?? string.Empty;
                _passwordInput.text = endpoint.RuntimeConfig.AuthPassword ?? string.Empty;
                await sceneSwitcher.FadeOutAsync();
                if (_lifecycleClosed) return;
 
                var authSessionTask = default(ValueTask<(string, AuthRequestResponse)>);
                var authCheckTask = default(ValueTask<EndpointResponse>?);
                var authProcessFlag = AUTH_FLAG_REQUESTING;
                var attempt = BeginAuthAttempt(endpoint);
                var authRequestId = string.Empty;
                var authCheckCooldownSec = 2d;
                authSessionTask = RegistryOwnedAuthSession(attempt);
                while (true)
                {
                    if (!IsCurrent(attempt)) return;
                    _isReady = true;
                    _usernameInput.readOnly = false;
                    _passwordInput.readOnly = false;
                    var isRefreshQRCodeRequested = InputManager.IsSensorPressedInThisFrame(SensorArea.A6) ||
                                                   InputManager.IsSensorPressedInThisFrame(SensorArea.B6) ||
                                                   InputManager.IsSensorPressedInThisFrame(SensorArea.E7);
                    try
                    {
                        if (authProcessFlag == AUTH_FLAG_REQUESTING)
                        {
                            if(authSessionTask.IsCompletedSuccessfully)
                            {
                                var (location, authRsp) = authSessionTask.Result;
                                var qrCodeData = _qrGenerator.CreateQrCode(location, ECCLevel.Q);
                                var qrCode = new PngByteQRCode(qrCodeData);
                                var pngBytes = qrCode.GetGraphic(20,false);
                                var texture = new Texture2D(0, 0);
                                authRequestId = authRsp.RequestId;

                                texture.LoadImage(pngBytes);

                                _qrCodeRawImage.texture = texture;
                                _qrCodeLoading.SetActive(false);
                                _qrCodeErrorIcon.SetActive(false);
                                _qrCodeRawImage.color = Color.white;
                                _nfcPublisher?.Publish(location);

                                authProcessFlag = AUTH_FLAG_WAIT_FOR_PERMIT;
                            }
                            else if(authSessionTask.IsCompleted)// Faulted or canceled
                            {
                                authProcessFlag = AUTH_FLAG_ERROR;
                                _nfcPublisher?.Clear();
                                _qrCodeErrorIcon.SetActive(true);
                                _qrCodeLoading.SetActive(false);
                                MajDebug.LogException(authSessionTask.AsTask().Exception);
                            }
                        }
                        else if (authProcessFlag == AUTH_FLAG_WAIT_FOR_PERMIT)
                        {
                            if(authCheckTask is ValueTask<EndpointResponse> task)
                            {
                                if (task.IsCompleted && !task.IsCompletedSuccessfully)
                                {
                                    MajDebug.LogError("Auth check failed");
                                    MajDebug.LogException(task.AsTask().Exception);
                                }
                                else if(task.IsCompletedSuccessfully)
                                {
                                    var rsp = task.Result;
                                    if (rsp.StatusCode is HttpStatusCode.OK)
                                    {
                                        _nfcPublisher?.Clear();
                                        MajDebug.LogDebug("User has granted authorization");
                                        _loading.SetActive(true);
                                        _isReady = false;
                                        MajDebug.LogDebug("Checking login status");
                                        var getUserInfoTask = FetchUserDataAsync(endpoint, _sceneCancellation.Token);
                                        while(!getUserInfoTask.IsCompleted)
                                        {
                                            if (!IsCurrent(attempt)) return;
                                            await UniTask.Yield();
                                        }
                                        if (!IsCurrent(attempt)) return;
                                        MajDebug.LogInfo("Logged in");
                                        endpoint.RuntimeConfig.AuthMethod = NetAuthMethodOption.QRCode;
                                        var userInfo = (UserSummary?)null;
                                        var userScores = Array.Empty<MajNetAccountSongScore>();
                                        if(getUserInfoTask.IsCompletedSuccessfully)
                                        {
                                            var userData = getUserInfoTask.Result;
                                            userInfo = userData.Summary;
                                            userScores = userData.Scores;
                                        }
                                        await UpdateApiEndpointRuntimeConfigAsync(endpoint, userInfo);
                                        if (!IsCurrent(attempt)) return;
                                        ScoreManager.LoadOnlineScores(userScores);
                                        RetireAuthAttempt(attempt, accepted: true);
                                        break;
                                    }
                                    else if (rsp.StatusCode is HttpStatusCode.Accepted)
                                    {
                                        _nfcPublisher?.ConfirmPending(_nfcCheckGeneration);
                                        // Still waiting for user to authorize
                                        authCheckTask = null;
                                        MajDebug.LogDebug("No user has granted authorization");
                                    }
                                    else if(rsp.StatusCode is HttpStatusCode.NotFound)
                                    {
                                        _nfcPublisher?.Clear();
                                        MajDebug.LogDebug("The authorization session has expired.");
                                        isRefreshQRCodeRequested = true;
                                        authCheckTask = null;
                                    }
                                    else
                                    {
                                        MajDebug.LogError($"Auth check returned unexpected status code: {rsp.StatusCode}");
                                        authCheckTask = null;
                                    }
                                }
                            }
                            else
                            {
                                if(authCheckCooldownSec < 0)
                                {
                                    _nfcCheckGeneration = _nfcPublisher?.BeginCheck() ?? -1;
                                    authCheckTask = Online.AuthCheckAsync(endpoint, authRequestId, attempt.Token);
                                    authCheckCooldownSec = 2d;
                                    MajDebug.LogDebug("Checking the auth session");
                                }
                                else
                                {
                                    authCheckCooldownSec -= MajTimeline.UnscaledDeltaTime;
                                }                                
                            }
                        }

                        if (isRefreshQRCodeRequested && authProcessFlag is (AUTH_FLAG_WAIT_FOR_PERMIT or AUTH_FLAG_ERROR))
                        {
                            _nfcPublisher?.Clear();
                            RetireAuthAttempt(attempt);
                            attempt = BeginAuthAttempt(endpoint);
                            authProcessFlag = AUTH_FLAG_REQUESTING;
                            authCheckTask = null;
                            _nfcCheckGeneration = -1;
                            _qrCodeErrorIcon.SetActive(false);
                            _qrCodeLoading.SetActive(true);
                            _qrCodeRawImage.texture = null!;
                            _qrCodeRawImage.color = new Color(0.5f, 0.5f, 0.5f);
                            authRequestId = string.Empty;
                            authSessionTask = RegistryOwnedAuthSession(attempt);
                        }
                        //cancel button
                        if (InputManager.IsSensorClickCompletedInThisFrame(SensorArea.A5) ||
                            InputManager.IsButtonClickCompletedInThisFrame(ButtonZone.A5))
                        {
                            _nfcPublisher?.Clear();
                            RetireAuthAttempt(attempt);
                            if (!string.IsNullOrEmpty(authRequestId))
                            {
                                await sceneSwitcher.FadeInAsync();
                                if (_lifecycleClosed) return;
                            }
                            _usernameInput.DeactivateInputField();
                            _passwordInput.DeactivateInputField();
                            break;
                        }
                        //login button
                        else if (InputManager.IsSensorClickCompletedInThisFrame(SensorArea.A4) ||
                            InputManager.IsButtonClickCompletedInThisFrame(ButtonZone.A4) ||
                            (endpoint.AutoLogin == true
                            && SceneSwitcher.LastScene == MajScenes.Title
                            && !string.IsNullOrEmpty(endpoint.Username)
                            && !string.IsNullOrEmpty(endpoint.Password)))
                        {
                            _isReady = false;
                            Hint();
                            _usernameInput.readOnly = true;
                            _passwordInput.readOnly = true;

                            _usernameInput.DeactivateInputField();
                            _passwordInput.DeactivateInputField();

                            var username = _usernameInput.text;
                            var password = _passwordInput.text;
                            MajDebug.LogInfo("Trying to log in via Plain");
                            var task = Online.LoginAsync(endpoint, username, password);

                            _loading.SetActive(true);
                            while (!task.IsCompleted)
                            {
                                if (_lifecycleClosed) return;
                                await UniTask.Yield();
                            }
                            if (_lifecycleClosed) return;
                            _loading.SetActive(false);
                            if (!task.IsCompletedSuccessfully)
                            {
                                var e = task.AsTask().Exception;
                                MajDebug.LogException(e);
                                Hint(e.ToString(), true);
                                continue;
                            }
                            var rsp = task.Result;
                            if (!rsp.IsSuccessfully)
                            {
                                MajDebug.LogError($"Login failed:\nStatusCode:{rsp.StatusCode}\nErrorCode:{rsp.ErrorCode}\nMessage:{rsp.Message}");
                                var errMsg = rsp.ErrorCode switch
                                {
                                    HttpErrorCode.Timeout => "MAJTEXT_LOGIN_CONNECT_TIMEOUT",
                                    HttpErrorCode.InvalidRequest => rsp.Message,
                                    HttpErrorCode.Unreachable => "MAJTEXT_LOGIN_CONNECT_UNREACHABLE",
                                    HttpErrorCode.Unsuccessful => rsp.StatusCode switch
                                    {
                                        HttpStatusCode.Unauthorized => "MAJTEXT_ONLINE_USERNAME_OR_PASSWORD_INCORRECT",
                                        HttpStatusCode.MethodNotAllowed => "MAJTEXT_ONLINE_METHOD_NOT_ALLOWED",
                                        HttpStatusCode.Forbidden => "MAJTEXT_ONLINE_ACCESS_FORBIDDEN",
                                        _ => "MAJTEXT_LOGIN_UNKNOWN_ERROR"
                                    },
                                    _ => "MAJTEXT_LOGIN_UNKNOWN_ERROR"
                                };
                                Hint($"{"MAJTEXT_LOGIN_LOGIN_FAILED".i18n()}:\n{errMsg.i18n()}", true);
                                endpoint.AutoLogin = false;
                                continue;
                            }
                            else
                            {
                                _nfcPublisher?.Clear();
                                MajDebug.LogInfo("Logged in");
                                Hint("MAJTEXT_LOGIN_LOGIN_SUCCESS".i18n(), false);
                                _loading.SetActive(true);
                                var getUserInfoTask = FetchUserDataAsync(endpoint, _sceneCancellation.Token);
                                RetireAuthAttempt(attempt);
                                while(!getUserInfoTask.IsCompleted)
                                {
                                    if (_lifecycleClosed) return;
                                    await UniTask.Yield();
                                }
                                if (_lifecycleClosed) return;
                                endpoint.RuntimeConfig.AuthMethod = NetAuthMethodOption.Plain;
                                endpoint.RuntimeConfig.AuthUsername = username;
                                endpoint.RuntimeConfig.AuthPassword = password;
                                var userInfo = (UserSummary?)null;
                                var userScores = Array.Empty<MajNetAccountSongScore>();
                                if (getUserInfoTask.IsCompletedSuccessfully)
                                {
                                    userInfo = getUserInfoTask.Result.Summary;
                                    userScores = getUserInfoTask.Result.Scores;
                                }
                                Hint();
                                _loading.SetActive(false);
                                await UpdateApiEndpointRuntimeConfigAsync(endpoint, userInfo);
                                if (_lifecycleClosed) return;
                                ScoreManager.LoadOnlineScores(userScores);
                                break;
                            }
                        }
                    }
                    finally
                    {
                        await UniTask.Yield();
                    }
                }
                await sceneSwitcher.FadeInAsync();
                _isReady = false;
            }
            EnterList();
        }
        void EnterList()
        {
            if(_isExited)
            {
                return;
            }
            _nfcPublisher?.Clear();
            if (_qrCodeRawImage != null) _qrCodeRawImage.texture = null!;
            _lifecycleClosed = true;
            _sceneCancellation.Cancel();
            _sceneCancellation.Dispose();
            RetireAuthAttempt(_authAttempt);
            _isExited = true;
            RefreshListBackgroundAsync(refreshWholeList:SceneSwitcher.LastScene != MajScenes.Title);
        }
        static async void RefreshListBackgroundAsync(bool refreshWholeList = false)
        {
            var sceneSwitcher = MajInstances.SceneSwitcher;
            await sceneSwitcher.FadeInAsync();
            sceneSwitcher.SwitchScene("Empty", false);
            await UniTask.Delay(400);
            var progress = new Progress<string>();
            progress.ProgressChanged += (o, e) =>
            {
                MajInstances.SceneSwitcher.SetLoadingText(e);
            };
            if (refreshWholeList)
            {
                var task = SongStorage.RefreshAsync(progress);
                while (!task.IsCompleted)
                {
                    await UniTask.Yield();
                }
                if (!task.IsCompletedSuccessfully)
                {
                    sceneSwitcher.SetLoadingText("MAJTEXT_ERR_SCAN_CHARTS_FAILED".i18n(), Color.red);
                    await UniTask.Delay(3000);
                }
                else
                {
                    sceneSwitcher.SetLoadingText(string.Empty);
                }
            }
            var task2 = SongStorage.RefreshUserOnlineFavAsync(progress);
            while (!task2.IsCompleted)
            {
                await UniTask.Yield();
            }
            if (!task2.IsCompletedSuccessfully || !task2.Result)
            {
                sceneSwitcher.SetLoadingText("MAJTEXT_ERR_SCAN_ONLINE_FAV_COLLECTION_FAILED".i18n(), Color.red);
            }
            else
            {
                sceneSwitcher.SetLoadingText(string.Empty);
            }

            
            sceneSwitcher.SwitchScene("List", false);
        }
        async ValueTask<UserData> FetchUserDataAsync(ApiEndpoint endpoint, CancellationToken token = default)
        {
            var fetchUserInfoRsp = await Online.GetUserInfoAsync(endpoint, token);
            token.ThrowIfCancellationRequested();
            var userScores = await Online.GetUserScoresAsync(endpoint, token);
            var userInfo = default(UserSummary?);
            if(fetchUserInfoRsp.TryDeserialize(out var userInfoOut, out var e))
            {
                userInfo = userInfoOut;
            }
            else
            {
                MajDebug.LogError("Failed to get user info");
                MajDebug.LogException(e);
            }
            token.ThrowIfCancellationRequested();
            return new()
            {
                Summary = userInfo,
                Scores = userScores,
            };
        }
        async UniTask UpdateApiEndpointRuntimeConfigAsync(ApiEndpoint endpoint, UserSummary? userInfo)
        {
            if (_lifecycleClosed) return;
            var runtimeConfig = endpoint.RuntimeConfig;
            if (userInfo is not null)
            {
                MajDebug.LogInfo("Downloading user avatar...");
                var result = (UserSummary)userInfo;
                _loading.SetActive(true);
                Hint("MAJTEXT_LOGIN_DOWNLOADING_AVATAR".i18n(), false);
                var avatarTask = Online.GetUserIconAsync(endpoint, result.Username, onError: Hint);
                while (!avatarTask.IsCompleted)
                {
                    if (_lifecycleClosed) return;
                    await UniTask.Yield();
                }
                if (_lifecycleClosed) return;
                _loading.SetActive(false);
                if (avatarTask.IsCompletedSuccessfully && avatarTask.Result is not null)
                {
                    Hint();
                    runtimeConfig.Avatar = avatarTask.Result;
                    MajDebug.LogInfo("User avatar has been downloaded");
                }
                runtimeConfig.Username = result.Username;
            }
            else
            {
                runtimeConfig.Avatar = null;
                if(runtimeConfig.AuthMethod == NetAuthMethodOption.Plain)
                {
                    runtimeConfig.Username = runtimeConfig.Username;
                }
                else
                {
                    runtimeConfig.Username = "???";
                }
            }
        }
        async UniTask RevokeAuthSession(ApiEndpoint endpoint, string authId, CancellationToken token = default)
        {
            var revokeTask = Online.AuthRevokeAsync(endpoint, authId, token);
            while (!revokeTask.IsCompleted)
            {
                token.ThrowIfCancellationRequested();
                await UniTask.Yield();
            }
            if (revokeTask.IsCompletedSuccessfully && revokeTask.Result.IsSuccessfully)
            {
                MajDebug.LogInfo("Successfully revoked the authorization session");
            }
            else if (revokeTask.IsCompletedSuccessfully)
            {
                MajDebug.LogError("Revoking the authorization session was rejected; server expiry remains the fallback");
            }
            else if (revokeTask.IsFaulted)
            {
                MajDebug.LogError("Revoking the authorization session failed");
                MajDebug.LogException(revokeTask.AsTask().Exception);
            }
        }
        async ValueTask<(string, AuthRequestResponse)> RegistryAuthSession(ApiEndpoint endpoint, CancellationToken token = default)
        {
            await UniTask.SwitchToThreadPool();
            var rsp = default(EndpointResponse);
            if (!Online.IsMachineRegistered(endpoint))
            {
                try
                {
                    
                    rsp = await Online.RegisterAsync(endpoint, MajEnv.MachineInfo, token);
                    if (!rsp.IsSuccessfully)
                    {
                        throw _exception;
                    }
                }
                catch
                {
                    MajDebug.LogError("Failed to register QR Code login session");
                    throw;
                }                
            }       
            
            try
            {
                rsp = await Online.AuthRequestAsync(endpoint, token);
                if (!rsp.IsDeserializable || rsp.StatusCode != HttpStatusCode.Created)
                {
                    throw _exception;
                }
            }
            catch
            {
                MajDebug.LogError("Attempt to request authorization session failed");
                throw;
            }
            
            var location = string.Empty;
            if (rsp.Headers.TryGetValue("location", out var headers))
            {
                location = headers.FirstOrDefault() ?? string.Empty;
            }
            var e = default(Exception?);
            if (string.IsNullOrEmpty(location) || !rsp.TryDeserialize<AuthRequestResponse?>(out var authRsp, out e) || authRsp is null)
            {
                MajDebug.LogError("The server returned an invalid auth response (redacted)");
                throw _exception;
            }
            return (location, (AuthRequestResponse)authRsp);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Hint(string hintText = "", bool isError = false)
        {
            if (!string.IsNullOrEmpty(hintText))
            {
                _hintText.color = isError ? ErrorColor : SucceedColor;
            }
            _hintText.text = hintText;
        }

        readonly struct AuthRequestResponse
        {
            public string RequestId { get; init; }
        }
        readonly struct UserData
        {
            public UserSummary? Summary { get; init; }
            public MajNetAccountSongScore[] Scores { get; init; }
        }
    }
}
