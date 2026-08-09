using System;
using UnityEngine;

namespace InventoryUX.Runtime
{
    internal sealed class FailureCircuitBreaker
    {
        private readonly string _operation;
        private readonly float _retryDelaySeconds;
        private readonly float _logIntervalSeconds;
        private float _retryAfter;
        private float _nextLogAt;
        private int _suppressedFailures;

        internal FailureCircuitBreaker(
            string operation,
            float retryDelaySeconds = 10f,
            float logIntervalSeconds = 60f)
        {
            _operation = operation;
            _retryDelaySeconds = retryDelaySeconds;
            _logIntervalSeconds = logIntervalSeconds;
        }

        internal bool IsOpen => Time.realtimeSinceStartup < _retryAfter;

        internal void Reset()
        {
            _retryAfter = 0f;
        }

        internal void Trip(Exception exception)
        {
            float now = Time.realtimeSinceStartup;
            _retryAfter = now + _retryDelaySeconds;
            if (now < _nextLogAt)
            {
                _suppressedFailures++;
                return;
            }

            string suppressed = _suppressedFailures > 0
                ? $" ({_suppressedFailures} repeated failures suppressed)"
                : string.Empty;
            _suppressedFailures = 0;
            _nextLogAt = now + _logIntervalSeconds;
            Plugin.LogInstance.LogWarning(
                $"{_operation} paused for {_retryDelaySeconds:0} seconds after an error{suppressed}: {exception}");
        }
    }
}
