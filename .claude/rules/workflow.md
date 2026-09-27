## Правила рабочего процесса

- Базовая ветка — `master`. Всегда создавай отдельную ветку от актуального `master` перед внесением изменений.
- Допускаются следующие наименования веток: `feature/`, `bugfix/`, `hotfix/`.
- Форматы для коммитов (commit): `type: description` (feat, fix, refactor, test, docs, style, perf, build, ci, chore, revert).
- Создавай атомарные коммиты — одно логическое изменение на коммит.
- Перед коммитом собирай проект: `dotnet build src/Calabonga.Microservices.BackgroundWorkers/Calabonga.Microservices.BackgroundWorkers.csproj -c Release`. `dotnet test` — только если появится тестовый проект.
- Если требуется создать новые классы, проверь на наличие файлов с таким же названием в решении.

### Релиз
- Каждый push в `master` запускает CI, который публикует пакет на nuget.org. Мерж в `master` = релиз.
- Изменения кода библиотеки в PR сопровождай повышением `<Version>` в `.csproj` (SemVer: ломающее изменение публичного/protected API — major) и обновлением `<PackageReleaseNotes>`.
- Изменения, не затрагивающие пакет (например, `.claude/`, `.github/FUNDING.yml`), тоже запускают CI; без повышения версии `dotnet nuget push` упадёт на дубликате — это ожидаемо, но предупреди об этом.
- Не меняй секрет `NUGET_API_KEY` и шаги публикации в `.github/workflows/main.yml` без согласования.
