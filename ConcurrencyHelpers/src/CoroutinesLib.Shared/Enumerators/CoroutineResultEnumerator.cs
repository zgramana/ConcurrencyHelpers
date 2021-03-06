// ===========================================================
// Copyright (c) 2014-2015, Enrico Da Ros/kendar.org
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 
// * Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
// 
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// ===========================================================


using CoroutinesLib.Shared.Enums;
using System;
using System.Collections;
using System.Collections.Generic;
using CoroutinesLib.Shared.Exceptions;
using CoroutinesLib.Shared.Logging;

namespace CoroutinesLib.Shared.Enumerators
{


	public interface ICoroutineResultEnumerator : ICoroutineResult, ILoggable
	{
		void Dispose();
		bool MoveNext();
		void Reset();
		ICoroutineResult Current { get; }
	}

	public class CoroutineResultEnumerator : IEnumerator<ICoroutineResult>, ICoroutineResultEnumerator, INamedItem
	{
		private bool _started = false;
		private IEnumerator<ICoroutineResult> _base;
		private readonly TimeSpan _expireIn;
		private CoroutineResultEnumerator _child;

		public CoroutineResultEnumerator(string instanceName, IEnumerator<ICoroutineResult> baseEnumerator, TimeSpan? expireIn = null)
		{
			Log = NullLogger.Create();
			_base = baseEnumerator;
			_instanceName = instanceName;
			if (expireIn == null || _expireIn.TotalMilliseconds < 0.01)
			{
				_expireIn = TimeSpan.FromDays(4096);
			}
			_expiration = DateTime.UtcNow + _expireIn;
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		// NOTE: Leave out the finalizer altogether if this class doesn't 
		// own unmanaged resources itself, but leave the other methods
		// exactly as they are. 
		~CoroutineResultEnumerator()
		{
			// Finalizer calls Dispose(false)
			Dispose(false);
		}


		public string InstanceName
		{
			get
			{
				return _instanceName;
			}
			set
			{
				_instanceName = value;
			}
		}

		// The bulk of the clean-up code is implemented in Dispose(bool)
		protected virtual void Dispose(bool disposing)
		{
			if (!_started)
			{
				Log.Warning("Not started {0}", InstanceName);
			}
			if (disposing)
			{
				// free managed resources
				if (_base != null)
				{
					_base.Dispose();
					_base = null;
					if (_child != null)
					{
						_child.Dispose();
						_child = null;
					}
				}
			}
		}

		private readonly DateTime? _expiration;
		private string _instanceName;

		public bool MoveNext()
		{
			_started = true;
			var now = DateTime.UtcNow;
			if (now > _expiration)
			{
				throw new CoroutineTimeoutException(
				 string.Format("Timeout exception on '{0}', '{1}'.", InstanceName, now - _expiration.Value));
			}
			Current = CoroutineResult.Wait;
			if (MoveNextForChild())
			{
				return true;
			}
			return MoveNextForCurrent();
		}

		private bool MoveNextForChild()
		{
			if (_child == null) return false;
			if (_child.MoveNext())
			{
				var frb = _child.Current as FluentResultBuilder;
				if (frb != null && !frb.Type.HasFlag(FluentResultType.Waiting))
				{
					//Bubble up the fluent
					Current = _child.Current;
				}
				return true;
			}
			_child.MoveNext();
			_child.Dispose();
			_child = null;
			return false;
		}

		private bool MoveNextForCurrent()
		{
			//Completed with the default yield break
			if (!_base.MoveNext())
			{
				_base.MoveNext();
				return false;
			}
			//It is the current underlying enumerator result
			var current = _base.Current;

			VerifyChildStatus(current);

			var result = false;
			switch (current.ResultType)
			{
				case (ResultType.Wait):
					result = true;
					break;
				case (ResultType.Return):
					break;
				case (ResultType.YieldReturn):
					result = true;
					break;
				case (ResultType.YieldBreak):
					_base.MoveNext();
					break;
				case (ResultType.Enumerator):
					_child = (CoroutineResultEnumerator)current.Result;
					result = true;
					break;
				case (ResultType.FluentBuilder):
					SetupFluentResultEnumerator(current);
					result = true;
					break;
			}
			return result;
		}

		private void VerifyChildStatus(ICoroutineResult current)
		{
			if (_child != null)
			{
				if (current.ResultType == ResultType.FluentBuilder || current.ResultType == ResultType.Enumerator)
				{
					throw new CoroutinesLibException("Duplicate child enumerator instance.");
				}
			}
		}

		private void SetupFluentResultEnumerator(ICoroutineResult current)
		{
			var builder = (FluentResultBuilder)current;
			builder.Log = Log;
			if (builder.Type.HasFlag(FluentResultType.CoroutineFunction) && !builder.Type.HasFlag(FluentResultType.Waiting))
			{
				_child = new CoroutineResultEnumerator(builder.InstanceName, builder.Coroutine.Execute().GetEnumerator())
				{
					Log = Log
				};
			}
			else if (builder.Type.HasFlag(FluentResultType.Waiting))
			{
				_child = new CoroutineResultEnumerator(builder.InstanceName, builder.RunEnumerator().GetEnumerator())
				{
					Log = Log
				};
			}
			else
			{
				Current = builder;
			}
		}

		public void Reset()
		{
			_base.Reset();
		}

		public ICoroutineResult Current { get; private set; }

		object IEnumerator.Current
		{
			get { return Current; }
		}

		public ResultType ResultType
		{
			get { return ResultType.Enumerator; }
		}
		public object Result
		{
			get { return Current; }
		}

		public ILogger Log { get; set; }

		public string BuildRunningStatus()
		{
			var result = _instanceName;
			if (_child != null)
			{
				result += "->" + _child.BuildRunningStatus();
			}
			return result;
		}
	}
}
