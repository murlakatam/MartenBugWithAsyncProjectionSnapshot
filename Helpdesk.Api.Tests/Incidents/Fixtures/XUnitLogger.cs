using System;
using System.Text;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Helpdesk.Api.Tests.Incidents.Fixtures;

public sealed class XUnitLogger<T> : XUnitLogger, ILogger<T>
{
    public XUnitLogger(Func<ITestOutputHelper> testOutputHelper, LoggerExternalScopeProvider scopeProvider)
        : base(testOutputHelper, scopeProvider, typeof(T).FullName!)
    {
    }
}

public sealed class XUnitLoggerFactory(Func<ITestOutputHelper?> testOutputHelper) : ILoggerFactory
{
    public void Dispose()
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XUnitLogger(testOutputHelper, new LoggerExternalScopeProvider(), categoryName);
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }
}

public sealed class XUnitLoggerProvider(Func<ITestOutputHelper?> testOutputHelper) : ILoggerProvider
{
    private readonly LoggerExternalScopeProvider _scopeProvider = new();

    public ILogger CreateLogger(string categoryName)
    {
        return new XUnitLogger(testOutputHelper, _scopeProvider, categoryName);
    }

    public void Dispose()
    {
    }
}

public class XUnitLogger : ILogger
{
    private readonly Func<ITestOutputHelper?> _testOutputHelper;
    private readonly string _categoryName;
    private readonly LoggerExternalScopeProvider _scopeProvider;

    public static ILogger CreateLogger(Func<ITestOutputHelper?> testOutputHelper)
    {
        return new XUnitLogger(
            testOutputHelper,
            new LoggerExternalScopeProvider(),
            "");
    }

    public static ILogger<T> CreateLogger<T>(Func<ITestOutputHelper?> testOutputHelper)
    {
        return new XUnitLogger<T>(testOutputHelper, new LoggerExternalScopeProvider());
    }

    public XUnitLogger(
        Func<ITestOutputHelper?> testOutputHelper,
        LoggerExternalScopeProvider scopeProvider,
        string categoryName)
    {
        _testOutputHelper = testOutputHelper;
        _scopeProvider = scopeProvider;
        _categoryName = categoryName;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return _scopeProvider.Push(state);
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var logger = _testOutputHelper();

        if (logger == null)
        {
            return;
        }

        var sb = new StringBuilder();

        sb.Append("[" + GetLogLevelString(logLevel) + "]")
            .Append(" [").Append(_categoryName).Append("] ")
            .Append(formatter(state, exception));

        if (exception != null)
        {
            sb.Append('\n').Append(exception);
        }

        _scopeProvider.ForEachScope(
            (scope, spState) =>
            {
                spState.Append("\n => ");
                spState.Append(scope);
            },
            sb);

        try
        {
            _testOutputHelper()?.WriteLine(sb.ToString());
        }
        catch (Exception)
        {
            // noop
            // Tries to log server shutting down after test finished causing test exception
        }
    }

    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
        };
    }
}