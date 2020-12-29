using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace MySqlBackupAgent.Services
{
    public class TimeZoneService
    {
        private readonly IJSRuntime _js;
        private TimeSpan? _offset = null;

        public TimeZoneService(IJSRuntime js)
        {
            _js = js;
        }

        public async Task LoadOffset()
        {
            if (_offset == null)
            {
                var minutes = await _js.InvokeAsync<int>("blazorGetTimezoneOffset");
                _offset = TimeSpan.FromMinutes(-minutes);
            }
        }

        public DateTime ToLocal(DateTime utc) => (DateTime) (_offset == null ? utc : utc + _offset);

    }
}