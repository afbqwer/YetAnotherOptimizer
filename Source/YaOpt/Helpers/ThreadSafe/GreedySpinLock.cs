using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace YaOpt.Helpers.ThreadSafe
{
	[StructLayout(LayoutKind.Auto, Size = 64)] // Fill a cache line to avoid false sharing
	public struct GreedySpinLock
	{
		private const int DEADLOCK_TIMEOUT = 10000; // The unit is Ms
		private int _state;
		private bool _panic;

		public void Enter()
		{
			if (_panic)
				Panic();

			var waitBeginTime = 0;
			while (true)
			{
				var spinCount = 0;
				while (Volatile.Read(ref _state) == 1)
				{
					var waitTime = 4 << Math.Min(spinCount, 10);
					Thread.SpinWait(waitTime);
					spinCount++;
					if (spinCount > 100)
						CheckDeadlock(ref waitBeginTime);
					if (spinCount > 10000) // Prevent the OS freezing
						Thread.Yield();
				}

				if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
					break;
			}
		}

		public void Exit()
		{
			Volatile.Write(ref _state, 0);
		}

		private void Panic()
		{
			throw new LockException("The program is unable to run due to a faulty synchronization lock. " +
									"Please report this issue to the YaOpt developers. If you wish to save " +
									"the game, it is recommended to save as a new save file.");
		}

		private void CheckDeadlock(ref int beginTime)
		{
			var currentTime = Environment.TickCount;
			if (beginTime == 0)
				beginTime = currentTime;
			if (currentTime - beginTime <= DEADLOCK_TIMEOUT)
				return;

			if (!_panic)
			{
				_panic = true;
				YaOptMod.Panic("The program encountered a faulty synchronization lock, which could be " +
							   "caused by a deadlock. A series of error logs containing call stack " +
							   "information will be printed below. Please report this issue to the YaOpt developers.");
			}
			Panic();
		}
	}
}
