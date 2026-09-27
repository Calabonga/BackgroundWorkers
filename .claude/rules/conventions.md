## Именование

- Используй PascalCase для классов, методов и свойств; camelCase для локальных переменных и параметров; `_camelCase` для приватных полей.
- Базовые классы воркеров называй с суффиксом `Base`: `HostedServiceBase`, `ScopedHostedServiceBase`, `ScheduledHostedServiceBase`.
- Пространство имён повторяет путь к папке от корня `Calabonga.Microservices.BackgroundWorkers` (`...BackgroundWorkers.Base`, `...BackgroundWorkers.Exceptions`).
- Исключения — в папке `Exceptions/`, имя `Worker[Причина]Exception`.
- Имя нового файла совпадает с именем класса. Существующие несовпадения (`ScopedBackgroundHostedService.cs` → `ScopedHostedServiceBase`, `ScheduledBackgroundHostedService.cs` → `ScheduledHostedServiceBase`) при обычных правках не переименовывай.
- Абстрактные методы-точки расширения называй глаголом с суффиксом `Async`: `ProcessAsync`, `ProcessInScopeAsync`.
- Свойства-настройки для наследников: `protected virtual` с префиксом `Is`/`Has` для `bool` (`IsExecuteOnServerRestart`, `IsDelayBeforeStart`).
- Существующие `#region Properties` сохраняй; в новом коде регионы не обязательны.
