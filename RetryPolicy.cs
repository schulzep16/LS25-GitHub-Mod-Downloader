using System;
using System.Threading.Tasks;
using Serilog;

namespace LS25ModDownloader
{
    public static class RetryPolicy
    {
        public static async Task<T> ExecuteAsync<T>(Func<Task<T>> action, int maxRetries, TimeSpan delay)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex)
                {
                    attempt++;
                    if (attempt >= maxRetries)
                    {
                        Log.Error(ex, "Maximale Anzahl an Versuchen erreicht.");
                        throw;
                    }
                    Log.Warning(ex, "Versuch {Attempt} fehlgeschlagen, retry in {Delay}...", attempt, delay);
                    await Task.Delay(delay);
                }
            }
        }
    }
}
