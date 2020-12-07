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

        static async Task Main(string[] args)
        {
            _connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");

            await InitializeAsync();

            Console.WriteLine("Starting tasks...");

            await ExecuteTestAsync();

            Console.WriteLine("Done!");
        }

        private static async Task ExecuteTestAsync()
        {
            using var cts = new CancellationTokenSource();
            var monitorTask = MonitorAsync(cts.Token);

            var tasks = new Task[4000];

            for (var i = 0; i < tasks.Length; i++)
            {
                tasks[i] = ExecuteLoopAsync();
            }

            await Task.WhenAll(tasks);

            cts.Cancel();
            await monitorTask;
        }

        private static async Task ExecuteLoopAsync()
        {
            var random = new Random();

            for (var i = 0; i < 200; i++)
            {
                await Task.Yield();

                var id = random.Next();

                try
                {
                    var result = await ExecuteTransactionAsync(id);
                    if (result != id)
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

        private static async Task<int> ExecuteTransactionAsync(int id)
        {
            var result = -1;

            using var connection = new SqlConnection(_connectionString);

            await connection.OpenAsync();
            var tx = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            var sql = @"select @Id as Id";

            var command = new SqlCommand(sql, connection, tx)
            {
                Transaction = tx,
                CommandTimeout = 1
            };
            command.Parameters.AddWithValue("Id", id);

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var columnIndex = reader.GetOrdinal("Id");
                    result = reader.GetInt32(columnIndex);
                    break;
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
                Console.WriteLine($"Processed: {successes + networkErrors + invalidResults,6} - Network errors: {networkErrors,6} - Invalid: {invalidResults,6}");
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
    }
}
