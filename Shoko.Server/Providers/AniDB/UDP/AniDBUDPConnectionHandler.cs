using System;
using System.Linq;
using System.Text;
using System.Timers;
using Microsoft.Extensions.Logging;
using Shoko.Server.Commands;
using Shoko.Server.Providers.AniDB.Interfaces;
using Shoko.Server.Providers.AniDB.UDP.Connection;
using Shoko.Server.Providers.AniDB.UDP.Exceptions;
using Shoko.Server.Providers.AniDB.UDP.Generic;
using Shoko.Server.Server;
using Shoko.Server.Settings.DI;
using Timer = System.Timers.Timer;

namespace Shoko.Server.Providers.AniDB.UDP
{
    public class AniDBUDPConnectionHandler : ConnectionHandler, IUDPConnectionHandler
    {
        private readonly IRequestFactory _requestFactory;
        IServiceProvider IUDPConnectionHandler.ServiceProvider => ServiceProvider;
        private IAniDBSocketHandler _socketHandler;

        public event EventHandler LoginFailed;

        public override double BanTimerResetLength => 1.5D;
        public override string Type => "UDP";
        public override UpdateType BanEnum => UpdateType.UDPBan;

        public string SessionID { get; set; }

        private string _cdnDomain = Constants.URLS.AniDB_Images_Domain;

        public string ImageServerUrl => string.Format(Constants.URLS.AniDB_Images, _cdnDomain);

        private SettingsProvider SettingsProvider { get; set; }

        private Timer _pulseTimer;

        private bool _isInvalidSession;
        public bool IsInvalidSession
        {
            get => _isInvalidSession;

            set
            {
                _isInvalidSession = value;
                UpdateState(new AniDBStateUpdate
                {
                    UpdateType = UpdateType.InvalidSession,
                    UpdateTime = DateTime.Now,
                    Value = value
                });
            }
        }

        private bool _isLoggedOn;
        public bool IsLoggedOn
        {
            get => _isLoggedOn;
            set => _isLoggedOn = value;
        }

        public bool IsNetworkAvailable { private set; get; }

        private DateTime LastAniDBPing { get; set; } = DateTime.MinValue;

        private DateTime LastAniDBMessageNonPing { get; set; } = DateTime.MinValue;

        private DateTime LastMessage =>
            LastAniDBMessageNonPing < LastAniDBPing ? LastAniDBPing : LastAniDBMessageNonPing;

        public AniDBUDPConnectionHandler(IRequestFactory requestFactory, ILoggerFactory loggerFactory, CommandProcessorGeneral queue, SettingsProvider settings, UDPRateLimiter rateLimiter) : base(loggerFactory, queue, rateLimiter)
        {
            _requestFactory = requestFactory;
            SettingsProvider = settings;
            InitInternal();
        }

        ~AniDBUDPConnectionHandler()
        {
            Logger.LogInformation("Disposing AniDBUDPConnectionHandler...");
            CloseConnections();
        }

        public void ExtendBanTimer(int time, string message) => base.ExtendBanTimer(time, message);

        public bool Init(string username, string password, string serverName, ushort serverPort, ushort clientPort)
        {
            var settings = SettingsProvider.Settings;
            settings.AniDb.ServerAddress = serverName;
            settings.AniDb.ServerPort = serverPort;
            settings.AniDb.ClientPort = clientPort;
            InitInternal();

            if (!ValidAniDBCredentials(username, password)) return false;
            SetCredentials(username, password);
            return true;
        }

        private void InitInternal()
        {
            if (_socketHandler != null)
            {
                _socketHandler.Dispose();
                _socketHandler = null;
            }

            var settings = SettingsProvider.Settings;
            _socketHandler = new AniDBSocketHandler(_loggerFactory, settings.AniDb.ServerAddress, settings.AniDb.ServerPort, settings.AniDb.ClientPort);
            _isLoggedOn = false;

            IsNetworkAvailable = _socketHandler.TryConnection();

            _pulseTimer = new Timer {Interval = 5000, Enabled = true, AutoReset = true};
            _pulseTimer.Elapsed += PulseTimerElapsed;

            Logger.LogInformation("starting ping timer...");
            _pulseTimer.Start();
        }

        private void PulseTimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                var tempTimestamp = DateTime.Now - LastMessage;
                if (ExtendPauseSecs.HasValue && tempTimestamp.TotalSeconds >= ExtendPauseSecs.Value)
                    ResetBanTimer();

                if (!_isLoggedOn) return;

                // don't ping when AniDB is taking a long time to respond
                if (_socketHandler.IsLocked) return;

                var nonPingTimestamp = DateTime.Now - LastAniDBMessageNonPing;
                var pingTimestamp = DateTime.Now - LastAniDBPing;
                tempTimestamp = DateTime.Now - LastMessage;

                // if we haven't sent a command for 45 seconds, send a ping just to keep the connection alive
                if (tempTimestamp.TotalSeconds >= Constants.PingFrequency &&
                    pingTimestamp.TotalSeconds >= Constants.PingFrequency &&
                    !IsBanned && !ExtendPauseSecs.HasValue)
                {
                    var ping = _requestFactory.Create<RequestPing>();
                    ping.Execute();
                }

                if (nonPingTimestamp.TotalSeconds > Constants.ForceLogoutPeriod) // after 10 minutes
                {
                    ForceLogout();
                }
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "{Message}", exception);
            }
        }

        /// <summary>
        /// Actually get data from AniDB
        /// </summary>
        /// <param name="command">The request to be made (AUTH user=baka&amp;pass....)</param>
        /// <param name="needsUnicode">Only for Login, specify whether to ask for UTF16</param>
        /// <param name="disableLogging">Some commands have sensitive data</param>
        /// <param name="isPing">is it a ping command</param>
        /// <returns></returns>
        public string CallAniDBUDP(string command, bool needsUnicode = true, bool disableLogging = false, bool isPing = false)
        {
            // Steps:
            // 1. Check Ban state and throw if Banned
            // 2. Check Login State and Login if needed
            // 3. Actually Call AniDB

            // Check Ban State
            // Ideally, this will never happen, as we stop the queue and attempt a graceful rollback of the command
            if (IsBanned) throw new AniDBBannedException { BanType = UpdateType.UDPBan, BanExpires = BanTime?.AddHours(BanTimerResetLength) };
            // TODO Low Priority: We need to handle Login Attempt Decay, so that we can try again if it's not just a bad user/pass
            // It wasn't handled before, and it's not caused serious problems
            
            // if we got here and it's invalid session, then it already failed to re-log
            if (IsInvalidSession) throw new NotLoggedInException();

            // Check Login State
            if (!Login())
                throw new NotLoggedInException();

            // Actually Call AniDB
            return CallAniDBUDPDirectly(command, needsUnicode, disableLogging, isPing);
        }

        public string CallAniDBUDPDirectly(string command, bool needsUnicode=true, bool disableLogging=false, bool isPing=false)
        {
            // 1. Call AniDB
            // 2. Decode the response, converting Unicode and decompressing, as needed
            // 3. Check for an Error Response
            // 4. Return a pretty response object, with a parsed return code and trimmed string
            var encoding = Encoding.ASCII;
            if (needsUnicode) encoding = new UnicodeEncoding(true, false);

            RateLimiter.EnsureRate();
            var start = DateTime.Now;

            if (!disableLogging)
            {
                Logger.LogTrace("AniDB UDP Call: (Using {Unicode}) {Command}", needsUnicode ? "Unicode" : "ASCII", command);
            }

            var sendByteAdd = encoding.GetBytes(command);
            StampLastMessage(isPing);
            var byReceivedAdd = _socketHandler.Send(sendByteAdd);
            StampLastMessage(isPing);

            if (byReceivedAdd.All(a => a == 0))
            {
                // we are probably banned or have lost connection. We can't tell the difference, so we're assuming ban
                IsBanned = true;
                throw new AniDBBannedException { BanType = UpdateType.UDPBan, BanExpires = BanTime?.AddHours(BanTimerResetLength) };
            }

            // decode
            var decodedString = GetEncoding(byReceivedAdd).GetString(byReceivedAdd, 0, byReceivedAdd.Length);
            if (decodedString[0] == 0xFEFF) // remove BOM
                decodedString = decodedString[1..];

            var ts = DateTime.Now - start;
            if (!disableLogging) Logger.LogTrace("AniDB Response: Received in {Time:ss'.'ffff}s\n{DecodedString}", ts, decodedString);
            return decodedString;
        }
        
        public void ForceReconnection()
        {
            try
            {
               ForceLogout(); 
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to logout: {Message}", ex);
            }

            try
            {
                CloseConnections();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to close socket: {Message}", ex);
            }
            
            try
            {
                InitInternal();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to reinitialize socket: {Message}", ex);
            }
        }

        private void StampLastMessage(bool isPing)
        {
            if (isPing)
                LastAniDBPing = DateTime.Now;
            else
                LastAniDBMessageNonPing = DateTime.Now;
        }

        /// <summary>
        /// Determines an encoded string's encoding by analyzing its byte order mark (BOM).
        /// Defaults to ASCII when detection of the text file's endianness fails.
        /// </summary>
        /// <param name="data">Byte array of the encoded string</param>
        /// <returns>The detected encoding.</returns>
        private static Encoding GetEncoding(byte[] data)
        {
            if (data.Length < 4) return Encoding.ASCII;
            // Analyze the BOM
#pragma warning disable SYSLIB0001
            if (data[0] == 0x2b && data[1] == 0x2f && data[2] == 0x76) return Encoding.UTF7;
            if (data[0] == 0xef && data[1] == 0xbb && data[2] == 0xbf) return Encoding.UTF8;
            if (data[0] == 0xff && data[1] == 0xfe) return Encoding.Unicode; //UTF-16LE
            if (data[0] == 0xfe && data[1] == 0xff) return Encoding.BigEndianUnicode; //UTF-16BE
            if (data[0] == 0 && data[1] == 0 && data[2] == 0xfe && data[3] == 0xff) return Encoding.UTF32;
            return Encoding.ASCII;
#pragma warning restore SYSLIB0001
        }

        public void ForceLogout()
        {
            if (!_isLoggedOn) return;
            if (IsBanned)
            {
                _isLoggedOn = false;
                SessionID = null;
                return;
            }
            Logger.LogTrace("Logging Out");
            try
            {
                _requestFactory.Create<RequestLogout>().Execute();
            }
            catch
            {
                // ignore
            }
            _isLoggedOn = false;
            SessionID = null;
        }

        public void CloseConnections()
        {
            _pulseTimer?.Stop();
            _pulseTimer = null;
            if (_socketHandler == null) return;
            Logger.LogInformation("AniDB UDP Socket Disposing...");
            _socketHandler.Dispose();
            _socketHandler = null;
        }

        public bool Login()
        {
            var settings = SettingsProvider.Settings;
            if (Login(settings.AniDb.Username, settings.AniDb.Password)) return true;

            try
            {
                Logger.LogTrace("Failed to login to AniDB. Issuing a Logout command and retrying");
                ForceLogout();
                return Login(settings.AniDb.Username, settings.AniDb.Password);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "{Message}", e);
            }

            return false;
        }

        private bool Login(string username, string password)
        {
            // check if we are already logged in
            if (IsLoggedOn) return true;

            if (!ValidAniDBCredentials(username, password))
            {
                LoginFailed?.Invoke(this, null!);
                return false;
            }

            Logger.LogTrace("Logging in");
            UDPResponse<ResponseLogin> response;
            try
            {
                var login = _requestFactory.Create<RequestLogin>(
                    r =>
                    {
                        r.Username = username;
                        r.Password = password;
                    }
                );
                response = login.Execute();
            }
            catch (UnexpectedUDPResponseException)
            {
                Logger.LogTrace("Received an UnexpectedUDPResponseException on Login. This usually happens because of an unexpected shutdown. Relogging using Unicode");
                var login = _requestFactory.Create<RequestLogin>(
                    r =>
                    {
                        r.Username = username;
                        r.Password = password;
                        r.UseUnicode = true;
                    }
                );
                response = login.Execute();
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Unable to login to AniDB: {Ex}", e);
                response = new UDPResponse<ResponseLogin>();
            }

            switch (response.Code)
            {
                case UDPReturnCode.LOGIN_FAILED:
                    SessionID = null;
                    IsInvalidSession = true;
                    IsLoggedOn = false;
                    Logger.LogError("AniDB Login Failed: invalid credentials");
                    LoginFailed?.Invoke(this, null!);
                    break;
                case UDPReturnCode.LOGIN_ACCEPTED:
                    SessionID = response.Response.SessionID;
                    _cdnDomain = response.Response.ImageServer;
                    IsLoggedOn = true;
                    IsInvalidSession = false;
                    return true;
                default:
                    SessionID = null;
                    IsLoggedOn = false;
                    IsInvalidSession = true;
                    break;
            }

            return false;
        }

        public bool TestLogin(string username, string password)
        {
            if (!ValidAniDBCredentials(username, password)) return false;
            var result = Login(username, password);
            if (result) ForceLogout();
            return result;
        }

        public bool SetCredentials(string username, string password)
        {
            if (!ValidAniDBCredentials(username, password)) return false;
            var settings = SettingsProvider.Settings;
            settings.AniDb.Username = username;
            settings.AniDb.Password = password;
            settings.SaveSettings();
            return true;
        }

        public bool ValidAniDBCredentials(string user, string pass)
        {
            if (string.IsNullOrEmpty(user)) return false;
            if (string.IsNullOrEmpty(pass)) return false;
            return true;
        }
    }
}
