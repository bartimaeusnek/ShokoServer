using Microsoft.Extensions.Logging;
using Shoko.Server.Providers.AniDB.Interfaces;
using Shoko.Server.Providers.AniDB.UDP.Generic;

namespace Shoko.Server.Providers.AniDB.UDP.Connection
{
    public class RequestLogout : UDPRequest<Void>
    {
        // Normally we would override Execute, but we are always logged in here, and Login() just returns if we are
        protected override string BaseCommand => "LOGOUT";
        protected override UDPResponse<Void> ParseResponse(UDPResponse<string> response)
        {
            var code = response.Code;
            return new UDPResponse<Void> {Code = code};
        }

        public RequestLogout(ILoggerFactory loggerFactory, IUDPConnectionHandler handler) : base(loggerFactory, handler)
        {
        }
    }
}
