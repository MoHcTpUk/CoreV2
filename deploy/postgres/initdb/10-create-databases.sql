-- Создание баз данных, к которым подключаются сервисы Core.
--
-- ЗАЧЕМ. Официальный образ postgres создаёт ровно ОДНУ базу: `$POSTGRES_DB`, а если
-- переменная не задана — базу с именем `$POSTGRES_USER`. Баз `identity`, `users` и
-- `sample` в свежем кластере нет, `Database.MigrateAsync()` их не создаёт (миграции
-- требуют уже существующей БД), поэтому без этого файла все сервисы на пустом томе
-- падают с `3D000: database "..." does not exist` и уходят в restart-loop.
--
-- СПИСОК БАЗ соответствует строкам подключения (`ConnectionStrings:DefaultConnection`):
--   identity — Identity.Service, Identity.WebApi (обе используют одну базу)
--   users    — Users.Service
--   sample   — Sample.Service
-- Источники: src/backend/Core/Services/*/appsettings.json, appsettings.Local.json,
--            deploy/docker-compose.app.local.yml, deploy/docker-compose.app.stage.yml.
-- Добавили новый вертикальный срез со своей БД — допишите её в VALUES ниже.
--
-- КОГДА ВЫПОЛНЯЕТСЯ.
--   1. Автоматически: файл смонтирован в /docker-entrypoint-initdb.d
--      (deploy/docker-compose.infra.yml). Entrypoint выполняет его ТОЛЬКО при первой
--      инициализации пустого каталога данных.
--   2. Принудительно: deploy/ensure-databases.sh скармливает этот же файл работающему
--      контейнеру. Так сходится стенд, у которого том postgres_data уже существует
--      (на нём init-скрипты больше никогда не запускаются). start_local.sh и
--      start_stage.sh вызывают его после подъёма infra и до подъёма app.
--
-- ИДЕМПОТЕНТНОСТЬ. `CREATE DATABASE` нельзя обернуть ни в транзакцию, ни в DO-блок,
-- поэтому команда генерируется как текст и выполняется через `\gexec` только для тех
-- имён, которых ещё нет в pg_database. Повторный запуск на готовом кластере —
-- no-op, ошибку `42P04: database already exists` он не выдаёт.
SELECT format('CREATE DATABASE %I', d.name)
FROM (VALUES ('identity'), ('users'), ('sample')) AS d(name)
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = d.name)
\gexec
