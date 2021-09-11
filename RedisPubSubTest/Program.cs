using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;

namespace RedisPubSubTest
{
	class Program
	{
		const int MaxAttempts = 20;

		static async Task Main()
		{
			Console.ForegroundColor = ConsoleColor.Cyan;

			for (var i = 0; i < MaxAttempts; i++)
			{
				await Test(i);
				await Task.Delay(500);
			}

			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("All test completed successfully!");
			Console.ForegroundColor = ConsoleColor.White;
		}


		private static async ValueTask Test(int testNumber)
		{
			var testName = $"TEST-{testNumber:00}";
			RedisHelper.ResetState();

			var stopwatch = new Stopwatch();

			var connection = RedisHelper.GetConnection();
			var subscriber = connection.GetSubscriber();
			subscriber.Subscribe(testName)
				.OnMessage(async (channelMessage) =>
				{
					stopwatch.Stop();
					Console.WriteLine($"{testName}: Message received! Time: {stopwatch.Elapsed.TotalMilliseconds:0.0}ms Message: {channelMessage.Message}");
					RedisHelper.DebugInfo(connection);
					// Simulate full-example code that is async
					await Task.CompletedTask;
				});

			var completionSource = new TaskCompletionSource<bool>();
			connection.GetSubscriber().Subscribe(testName).OnMessage(channelMessage =>
			{
				completionSource.SetResult(true);
			});
			
			await Task.Delay(500);

			stopwatch.Start();
			await subscriber.PublishAsync(testName, $"Hello {testNumber:00}", CommandFlags.FireAndForget);

			var succeedingTask = await Task.WhenAny(completionSource.Task, Task.Delay(TimeSpan.FromSeconds(10)));
			if (!succeedingTask.Equals(completionSource.Task))
			{
				stopwatch.Stop();
				Console.ForegroundColor = ConsoleColor.Red;
				RedisHelper.DebugInfo(connection);
				Debug.Fail($"{testName}: Subscriber response took too long ({stopwatch.Elapsed.TotalMilliseconds})");
			}
		}
	}

	public static class RedisHelper
	{
		public static string Endpoint => Environment.GetEnvironmentVariable("REDIS_ENDPOINT") ?? "localhost:6379";

		private static readonly ConcurrentQueue<string> Errors = new();

		static RedisHelper()
		{
			var connection = GetConnection();
			connection.ErrorMessage += (sender, args) =>
			{
				Errors.Enqueue(args.Message);
			};
			connection.InternalError += (sender, args) =>
			{
				if (args.Exception is not null)
				{
					Errors.Enqueue(args.Exception.Message);
				}
			};
		}

		public static ConnectionMultiplexer GetConnection()
		{
			var config = new ConfigurationOptions
			{
				AllowAdmin = true
			};
			config.EndPoints.Add(Endpoint);
			return ConnectionMultiplexer.Connect(config);
		}

		/// <summary>
		/// Flushes Redis and resets the state of error logging
		/// </summary>
		public static void ResetState()
		{
			GetConnection().GetServer(Endpoint).FlushDatabase();

			//.NET Framework doesn't support `Clear()` on Errors so we do it manually
			while (!Errors.IsEmpty)
			{
				Errors.TryDequeue(out _);
			}
		}

		public static void DebugInfo(IConnectionMultiplexer connection)
		{
			var currentColour = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.DarkGray;
			Console.WriteLine("== Redis Connection Status ==");
			Console.WriteLine(connection.GetStatus());

			Console.WriteLine("== Errors (Redis and Internal) ==");
			if (Errors.IsEmpty)
			{
				Console.WriteLine("None");
			}
			while (!Errors.IsEmpty)
			{
				if (Errors.TryDequeue(out var message))
				{
					Console.WriteLine(message);
				}
			}
			Console.ForegroundColor = currentColour;
		}
	}
}
