# AsyncRAT Модернизация под .NET 9.0

## Обзор
Данный документ описывает полную модернизацию кодовой базы AsyncRAT под .NET 9.0 с использованием операторов верхнего уровня (top-level statements) и современных возможностей фреймворка.

## Ключевые улучшения .NET 9.0

### 1. Операторы верхнего уровня (Top-Level Statements)

#### Server/Program.cs
```csharp
// Вместо традиционного класса Program
using var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services => {
        services.AddLogging(builder => {
            builder.AddConsole();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        services.AddSingleton<IAsyncRATLogger, AsyncRATLogger>();
    })
    .Build();

// Основная логика приложения
try {
    await logger.LogAsync(LogLevel.Information, "AsyncRAT Server starting up...");
    // ... остальная логика
}
catch (Exception ex) {
    await logger.LogAsync(LogLevel.Critical, "Fatal error", ex);
}

// Локальные функции для организации кода
static async Task InitializeResourcesAsync(IAsyncRATLogger logger) {
    // Инициализация ресурсов
}
```

#### Client/Program.cs
```csharp
// Аналогичная структура для клиента
using var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services => {
        services.AddLogging(builder => {
            builder.AddEventLog();
            builder.SetMinimumLevel(LogLevel.Warning);
        });
        services.AddSingleton<IClientLogger, ClientLogger>();
    })
    .Build();

// Основная логика клиента с локальными функциями
```

### 2. File-Scoped Namespaces

Все новые файлы используют file-scoped namespaces для более чистого кода:

```csharp
namespace Server.Helper;

public sealed class AsyncRATLogger : IAsyncRATLogger
{
    // Реализация без дополнительных отступов
}
```

### 3. Современные Record Types

#### Конфигурация как Record
```csharp
public sealed record ServerConfiguration
{
    public required string Version { get; init; } = "AsyncRAT 0.6.0 (.NET 9.0)";
    public required string CertificatePath { get; init; } = "ServerCertificate.p12";
    public IReadOnlyList<string> BlockedIPs { get; init; } = [];
    
    public static ServerConfiguration CreateDefault() => new()
    {
        Version = "AsyncRAT 0.6.0 (.NET 9.0)",
        // ... остальные свойства
    };
}
```

#### Статистика как Record Struct
```csharp
public readonly record struct LogStatistics(
    long TotalEntries,
    long ErrorCount,
    long WarningCount,
    DateTime LastLogTime,
    long LogFileSizeBytes
);
```

### 4. Новый Lock Type (.NET 9.0)

Использование нового типа `Lock` вместо `object` для синхронизации:

```csharp
private static readonly Lock _configLock = new();

// Использование с using statement
using var lockScope = _configLock.EnterScope();
```

### 5. Collection Expressions

Современный синтаксис для коллекций:

```csharp
public IReadOnlyList<string> BlockedIPs { get; init; } = [];
```

### 6. UTF-8 String Literals

```csharp
private static readonly byte[] _entropy = "AsyncRAT-Config-Salt-v2"u8.ToArray();
```

### 7. Pattern Matching Improvements

```csharp
ServerCertificate = ModernConfigurationManager.Current.CertificatePath switch
{
    var path when File.Exists(path) => new X509Certificate2(path, password),
    _ => null
};
```

### 8. Современные Async Patterns

#### IAsyncEnumerable для потоковой обработки
```csharp
public async IAsyncEnumerable<ModernClient> GetConnectedClientsAsync(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    await foreach (var client in GetClientsAsync(cancellationToken))
    {
        if (client.IsConnected)
            yield return client;
    }
}
```

#### PeriodicTimer для периодических задач
```csharp
using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
while (!cancellationToken.IsCancellationRequested)
{
    await timer.WaitForNextTickAsync(cancellationToken);
    // Выполнение периодической задачи
}
```

### 9. High-Performance Networking

#### ArrayPool для управления памятью
```csharp
var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
try
{
    // Использование буфера
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
}
```

#### ReadOnlyMemory<byte> для эффективной передачи данных
```csharp
public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
{
    // Эффективная работа с памятью без копирования
}
```

#### Span<T> для операций без аллокаций
```csharp
var messageLength = BitConverter.ToInt32(headerBuffer.Span);
```

### 10. Microsoft.Extensions.* Integration

#### Dependency Injection
```csharp
services.AddSingleton<IAsyncRATLogger, AsyncRATLogger>();
services.AddLogging(builder => {
    builder.AddConsole();
    builder.AddDebug();
});
```

#### Structured Logging
```csharp
public interface IAsyncRATLogger
{
    Task LogAsync(LogLevel level, string message, Exception? exception = null, 
                  CancellationToken cancellationToken = default);
}
```

## Архитектурные улучшения

### 1. Современная система логирования

#### Интерфейс логгера
- Асинхронные методы логирования
- Поддержка CancellationToken
- Интеграция с Microsoft.Extensions.Logging
- Batch processing для производительности

#### Реализация
- Очередь сообщений для неблокирующего логирования
- Автоматическая ротация логов
- Thread-safe операции
- Статистика логирования

### 2. Конфигурационный менеджер

#### Особенности
- Record types для immutable конфигурации
- Шифрование конфигурации через DPAPI
- JSON сериализация с System.Text.Json
- Валидация конфигурации
- Автоматическое резервное копирование

### 3. Сетевая подсистема

#### ModernListener
- IAsyncDisposable для правильной очистки ресурсов
- Семафор для ограничения подключений
- IAsyncEnumerable для перечисления клиентов
- Мониторинг подключений с PeriodicTimer

#### ModernClient
- Очередь отправки сообщений
- ArrayPool для эффективного использования памяти
- ReadOnlyMemory<byte> для передачи данных
- SSL/TLS с современными протоколами

## Производительность

### Улучшения производительности:

1. **Управление памятью**
   - ArrayPool для буферов
   - Span<T> и Memory<T> для операций без аллокаций
   - Batch processing для уменьшения GC pressure

2. **Асинхронность**
   - Полностью асинхронные операции I/O
   - CancellationToken для отмены операций
   - ConfigureAwait(false) где необходимо

3. **Сетевая производительность**
   - TCP_NODELAY для низкой задержки
   - Оптимальные размеры буферов
   - Chunked transfer для больших сообщений

## Безопасность

### Улучшения безопасности:

1. **SSL/TLS**
   - TLS 1.2 и TLS 1.3
   - Правильная валидация сертификатов
   - Современные cipher suites

2. **Конфигурация**
   - Шифрование конфигурации через DPAPI
   - Валидация всех параметров
   - Безопасные значения по умолчанию

3. **Логирование**
   - Структурированное логирование
   - Отсутствие sensitive данных в логах
   - Контролируемое логирование клиента

## Совместимость

### Обратная совместимость:

1. **Legacy методы**
   - Помечены как `[Obsolete]`
   - Переадресация на новые реализации
   - Сохранение функциональности

2. **Глобальное состояние**
   - `Server.GlobalState` для доступа к MainForm
   - `Client.GlobalState` для управления состоянием

## Файловая структура

### Новые файлы:

**Server:**
- `Helper/IAsyncRATLogger.cs` - Интерфейс логгера
- `Helper/AsyncRATLogger.cs` - Реализация логгера
- `Helper/ModernConfigurationManager.cs` - Современный конфиг менеджер
- `Connection/ModernListener.cs` - Современный слушатель
- `Connection/ModernClient.cs` - Современный клиент

**Client:**
- `Helper/IClientLogger.cs` - Интерфейс клиентского логгера
- `Helper/ClientLogger.cs` - Реализация клиентского логгера

### Обновленные файлы:

- `Server/Program.cs` - Top-level statements
- `Client/Program.cs` - Top-level statements
- `Client/Settings.cs` - Улучшенная валидация

## Рекомендации по использованию

### Для разработчиков:

1. **Использование новых API:**
   ```csharp
   // Логирование
   await logger.InfoAsync("Operation completed");
   
   // Конфигурация
   var config = ModernConfigurationManager.Current;
   await ModernConfigurationManager.SaveConfigurationAsync(config);
   
   // Сеть
   await using var listener = new ModernListener(logger);
   await listener.StartAsync(port);
   ```

2. **Async/Await patterns:**
   - Всегда используйте async/await для I/O операций
   - Передавайте CancellationToken
   - Используйте ConfigureAwait(false) в библиотечном коде

3. **Memory Management:**
   - Используйте ArrayPool для больших буферов
   - Предпочитайте ReadOnlyMemory<byte> для передачи данных
   - Используйте using для IDisposable/IAsyncDisposable

### Для администраторов:

1. **Мониторинг:**
   - Проверяйте логи в директории `Logs/`
   - Используйте статистические методы для мониторинга
   - Настройте ротацию логов

2. **Конфигурация:**
   - Конфигурация автоматически шифруется
   - Резервные копии создаются автоматически
   - Используйте JSON формат для ручного редактирования

## Заключение

Модернизация под .NET 9.0 обеспечивает:

- **Современный код** с использованием новейших возможностей C# и .NET
- **Высокую производительность** через эффективное управление памятью
- **Лучшую безопасность** с современными криптографическими протоколами
- **Улучшенную поддерживаемость** благодаря структурированному коду
- **Обратную совместимость** с существующим функционалом

Код готов для использования в production среде с современными требованиями к безопасности и производительности.