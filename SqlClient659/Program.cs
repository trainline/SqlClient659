using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SqlClient659
{
    class Program
    {
        private static string _connectionString;

        private static int _successes;
        private static int _networkErrors;
        private static int _invalidResults;
        private static int _missingResults;

        static async Task Main(string[] args)
        {
            using var cts = new CancellationTokenSource();

            _connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");

            Console.CancelKeyPress += new ConsoleCancelEventHandler(CancelExecution);

            await InitializeAsync();

            await ExecuteTestAsync(cts.Token);

            Console.WriteLine("Done!");
        }

        private static async Task ExecuteTestAsync(CancellationToken cancellationToken)
        {
            using var monitorCts = new CancellationTokenSource();
            var monitorTask = MonitorAsync(monitorCts.Token);

            var tasks = new Task[4000];
            Console.WriteLine($"Starting {tasks.Length} tasks...");

            for (var i = 0; i < tasks.Length; i++)
            {
                tasks[i] = ExecuteLoopAsync(i, cancellationToken);
            }

            await Task.WhenAll(tasks);

            monitorCts.Cancel();
            await monitorTask;
        }

        private static async Task ExecuteLoopAsync(int id, CancellationToken cancellationToken)
        {
            await Task.Yield();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await ExecuteTransactionAsync(id);
                    if (!result.HasValue)
                    {
                        Interlocked.Increment(ref _missingResults);
                    }
                    else if (result != id)
                    {
                        Interlocked.Increment(ref _invalidResults);
                    }
                    else
                    {
                        Interlocked.Increment(ref _successes);
                    }
                }
                catch (Exception exception) when (exception is SqlException || exception is InvalidOperationException)
                {
                    Interlocked.Increment(ref _networkErrors);
                }
            }
        }

        private static async Task<int?> ExecuteTransactionAsync(int id)
        {
            int? result = null;

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var tx = connection.BeginTransaction(IsolationLevel.ReadCommitted);

            await using var command = new SqlCommand(@"select @Id as Id", connection, tx);
            command.CommandTimeout = 1;
            command.Parameters.AddWithValue("Id", id);

            using (var reader = await command.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    var columnIndex = reader.GetOrdinal("Id");
                    result = reader.GetInt32(columnIndex);
                }
            }

            await tx.CommitAsync();

            return result;
        }


        private static async Task InitializeAsync()
        {
            FlushFirewall();

            Console.WriteLine("Waiting for SQLServer...");
            await ReadinessCheckAsync();
            Console.WriteLine("Smoke test passed!");

            await TestFirewallAsync();

            SetupFirewall();
        }

        private static async Task ReadinessCheckAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Exception lastException = null;

            while (true)
            {
                if (cts.Token.IsCancellationRequested)
                {
                    Console.WriteLine(lastException.ToString());
                    throw lastException;
                }

                try
                {
                    await ExecuteTransactionAsync(1);
                    return;
                }
                catch (Exception exception)
                {
                    lastException = exception;
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        private static async Task MonitorAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                var successes = _successes;
                var networkErrors = _networkErrors;
                var invalidResults = _invalidResults;
                var missingResults = _missingResults;
                int total = successes + networkErrors + invalidResults + missingResults;

                Console.WriteLine($"Processed: {total,6} - Network errors: {networkErrors,6} - Missing: {missingResults, 6} - Invalid: {invalidResults,6}");
            }
        }

        private static void SetupFirewall()
        {
            var iptables = Process.Start("iptables-legacy", "-A INPUT -p tcp --sport 1433 -m statistic --mode random --probability 0.1 -j DROP");
            iptables.WaitForExit();
        }

        private static void FlushFirewall()
        {
            var iptables = Process.Start("iptables-legacy", "-F");
            iptables.WaitForExit();
        }

        private static async Task TestFirewallAsync()
        {
            var iptables = Process.Start("iptables-legacy", "-A INPUT -p tcp --sport 1433 -j REJECT");
            iptables.WaitForExit();

            try
            {
                await ExecuteTransactionAsync(1);
                throw new Exception("iptables test failed!");
            }
            catch (SqlException)
            {
                Console.WriteLine("iptables test passed!");
            }

            FlushFirewall();
        }

        private static void CancelExecution(object sender, ConsoleCancelEventArgs args)
        {
            // Set the Cancel property to true to prevent the process from terminating.
            args.Cancel = true;
            Console.WriteLine("Requested cancellation..");
        }
    }
}
