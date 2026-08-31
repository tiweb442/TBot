using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {

	public abstract class CelestialWorkerBase : ITBotWorker, ITBotCelestialWorker {
		protected readonly ITBotMain _tbotInstance;

		protected CancellationToken _ct = CancellationToken.None;
		protected Dictionary<string, Timer> timers = new();

		private SemaphoreSlim _sem = new SemaphoreSlim(1, 1);
		private readonly SemaphoreSlim _stopLock = new SemaphoreSlim(1, 1);
		private AsyncTimer _timer = null;
		
		private Celestial _celestial = null;
		private ITBotWorker _parentWorker = null;

		public ITBotWorker parentWorker {
			get {
				return (_parentWorker != null) ? _parentWorker : null;
			}
		}

		public Celestial celestial {
			get {
				return (_celestial != null) ? _celestial : null;
			}
		}

		public TimeSpan DueTime {
			get {
				return (_timer != null) ? _timer.DueTime : TimeSpan.Zero;
			}
		}
		public TimeSpan Period {
			get {
				return (_timer != null) ? _timer.Period : TimeSpan.Zero;
			}
		}

		public ConcurrentDictionary<Celestial, ITBotCelestialWorker> celestialWorkers => throw new NotImplementedException();

		public CelestialWorkerBase(ITBotMain parentInstance, ITBotWorker parentWorker, Celestial celestial) {
			_tbotInstance = parentInstance;
			_parentWorker = parentWorker;
			_celestial = celestial;
		}

		protected abstract Task Execute();
		public void DoLog(LogLevel level, string format) {
			_tbotInstance.log(level, GetLogSender(), format);
		}

		public async Task StartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime) {

			DoLog(LogLevel.Information, $"Starting Worker \"{GetWorkerName()}\"..");

			await StopWorker();

			_ct = ct;

			// TimeSpan periodSpan = TimeSpan.FromMilliseconds(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
			// ThreadName cannot be longer than 16 bytes, so
			string cutAlias = (_tbotInstance.InstanceAlias.Length > 6 ? _tbotInstance.InstanceAlias.Substring(0, 6) : _tbotInstance.InstanceAlias);
			_timer = new AsyncTimer(ExecutionWrapper, $"{cutAlias}{GetWorkerName()}");
			await _timer.StartAsync(ct, period, dueTime);
		}
		public async Task StartWorker(CancellationToken ct, TimeSpan dueTime) {
			await StartWorker(ct, Timeout.InfiniteTimeSpan, dueTime);
		}
		public async Task StopWorker() {
			await _stopLock.WaitAsync();
			try {
				RemoveAllTimers();
				if (_timer != null) {
					var timer = _timer;
					_timer = null;
					DoLog(LogLevel.Information, $"Closing Worker \"{GetWorkerName()}\"..");
					await timer.DisposeAsync();
					DoLog(LogLevel.Information, $"Worker \"{GetWorkerName()}\" closed!");
				}
			} finally {
				_stopLock.Release();
			}
		}
		public void ChangeWorkerPeriod(long periodMs) {
			if (periodMs >= int.MaxValue)
				periodMs = int.MaxValue;
			ChangeWorkerPeriod(TimeSpan.FromMilliseconds(periodMs));
		}
		public void ChangeWorkerPeriod(TimeSpan period) {
			if (_timer != null)
				_timer.ChangePeriod(period);
		}
		public void ChangeWorkerDueTime(TimeSpan dueTime) {
			if (_timer != null)
				_timer.ChangeDueTime(dueTime);
		}
		public void ChangeWorkerDueTime(long dueTimeMs) {
			if (_timer != null)
				_timer.ChangeDueTime(TimeSpan.FromMilliseconds(dueTimeMs));
		}
		public async void RestartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime) {
			DoLog(LogLevel.Information, $"Restarting Worker \"{GetWorkerName()}\"...");
			await StartWorker(ct, period, dueTime);
		}
		public bool IsWorkerRunning() {
			return (_timer != null) && (_timer.IsRunning);
		}
		public void SetSemaphore(SemaphoreSlim sem) {
			_sem.Dispose();
			_sem = sem;
		}
		public SemaphoreSlim GetSemaphore() {
			return _sem;
		}
		public async Task WaitWorker() {
			try {
				await _sem.WaitAsync(_ct);
			} catch (OperationCanceledException) {
			}
		}
		public void ReleaseWorker() {
			try {
				if (_sem.CurrentCount == 0)
					_sem.Release();
			} catch (SemaphoreFullException) {
				DoLog(LogLevel.Warning, $"{GetWorkerName()} already released...");
			}
		}

		public abstract bool IsWorkerEnabledBySettings();
		public abstract string GetWorkerName();
		public abstract Feature GetFeature();
		public abstract LogSender GetLogSender();



		protected Task EndExecution() {
			// This is meant to be called within the worker callback, so we can't await _timer to end
			ChangeWorkerPeriod(Timeout.InfiniteTimeSpan);
			return Task.CompletedTask;
		}

		protected virtual void LogSleepPause() {
			DoLog(LogLevel.Information, $"Sleeping... Ending {GetWorkerName()}");
		}

		private async Task ExecutionWrapper(CancellationToken ct) {

			if (_tbotInstance.UserData.isSleeping == true) {
				LogSleepPause();
				await EndExecution();
				return;
			} else if (IsWorkerEnabledBySettings() == false) {
				DoLog(LogLevel.Information, $"{GetWorkerName()} not enabled by settings. Ending...");
				await EndExecution();
				return;
			}

			bool acquired = false;
			try {
				try {
					await _sem.WaitAsync(_ct);
					acquired = true;
				} catch (OperationCanceledException) {
					return;
				}

				ct.ThrowIfCancellationRequested();

				await Execute();

				if (Period != Timeout.InfiniteTimeSpan) {
					DoLog(LogLevel.Information, $"Next {GetWorkerName()} execution in {Period}");
				}
				else {
					DoLog(LogLevel.Information, $"{GetWorkerName()} Stopped.");
				}

			} catch(OperationCanceledException) {
				// OK
			} finally {
				if (acquired)
					ReleaseWorker();
			}
		}

		private void RemoveAllTimers() {
			foreach (var tim in timers) {
				DoLog(LogLevel.Information, $"Deleting timer \"{tim.Key}\" for worker \"{GetWorkerName()}\"");
				tim.Value.Dispose();
			}
			timers.Clear();
		}
	}
}
