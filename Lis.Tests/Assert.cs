using System;
using System.Collections.Generic;

namespace Lis.Tests
{
    public static class Assert
    {
        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception($"Assert.Equal() Failure\nExpected: {expected}\nActual:   {actual}");
            }
        }

        public static void NotEqual<T>(T expected, T actual)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception($"Assert.NotEqual() Failure\nExpected different than: {expected}\nActual:                 {actual}");
            }
        }

        public static void True(bool condition, string? message = null)
        {
            if (!condition)
            {
                throw new Exception($"Assert.True() Failure. {message}");
            }
        }

        public static void False(bool condition, string? message = null)
        {
            if (condition)
            {
                throw new Exception($"Assert.False() Failure. {message}");
            }
        }

        public static void NotNull(object? value)
        {
            if (value == null)
            {
                throw new Exception("Assert.NotNull() Failure. Value was null.");
            }
        }

        public static void Null(object? value)
        {
            if (value != null)
            {
                throw new Exception($"Assert.Null() Failure. Value was not null: {value}");
            }
        }
    }
}
