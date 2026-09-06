#!/bin/bash
# Гарантирует, что базы из postgres/initdb/10-create-databases.sql существуют.
#
# ЗАЧЕМ ОТДЕЛЬНЫЙ СКРИПТ. Каталог /docker-entrypoint-initdb.d официальный образ
# postgres выполняет ТОЛЬКО при инициализации пустого каталога данных. Стенд,
# у которого том postgres_data уже создан (а на stage он создан), новый
# init-скрипт никогда не увидит и продолжит падать с `3D000: database "users"
# does not exist`. Этот скрипт прогоняет ровно тот же SQL-файл по работающему
# контейнеру, поэтому сходятся оба случая: чистый том и уже существующий.
#
# Скрипт идемпотентен: SQL создаёт только отсутствующие базы (проверка
# pg_database + \gexec), повторный запуск ничего не меняет.
#
# Вызывается из start_local.sh и start_stage.sh между подъёмом стека infra и
# стека app. Руками — так же: `bash deploy/ensure-databases.sh`.
#
# УЧЁТНЫЕ ДАННЫЕ берутся из окружения САМОГО КОНТЕЙНЕРА ($POSTGRES_USER /
# $POSTGRES_PASSWORD внутри него), а не из окружения хоста. Так у local и stage
# остаётся один источник истины — compose-файлы, дефолты `${POSTGRES_USER:-user}`
# не приходится дублировать здесь, и пароль не попадает в `ps` на хосте.

cd "$(dirname "$0")" || exit 1

SQL_FILE="postgres/initdb/10-create-databases.sql"
# Имя контейнера задачи Swarm: <стек>_<сервис>.<slot>.<task id>.
CONTAINER_FILTER="name=infra_postgres."
# Совокупно ~120 с: столько занимает первый старт postgres на пустом томе
# (initdb + выполнение init-скриптов) на медленном диске.
TIMEOUT_SECONDS=120

if [ ! -f "$SQL_FILE" ]; then
    echo "ERROR: не найден $SQL_FILE (скрипт должен лежать рядом с каталогом postgres/)" >&2
    exit 1
fi

echo "Ожидание готовности postgres (до ${TIMEOUT_SECONDS} с)..."
container=""
deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
while [ "$(date +%s)" -lt "$deadline" ]; do
    candidate=$(docker ps --filter "$CONTAINER_FILTER" --format '{{.ID}}' | head -n 1)
    if [ -n "$candidate" ] && docker exec "$candidate" \
            sh -c 'pg_isready -q -U "$POSTGRES_USER"' 2>/dev/null; then
        container="$candidate"
        break
    fi
    sleep 2
done

if [ -z "$container" ]; then
    echo "ERROR: postgres не поднялся за ${TIMEOUT_SECONDS} с — базы не созданы." >&2
    echo "Проверьте: docker service ps infra_postgres; docker service logs infra_postgres" >&2
    exit 1
fi

# Подключаемся к служебной базе `postgres`: она есть в любом кластере, в отличие
# от базы с именем пользователя (её создаёт entrypoint) и от создаваемых здесь.
# Соединение идёт по unix-сокету внутри контейнера, наружу не выходит.
# $POSTGRES_USER / $POSTGRES_PASSWORD раскрывает sh ВНУТРИ контейнера, не хост:
# строка в одинарных кавычках до него доезжает нетронутой.
psql_cmd='PGPASSWORD="$POSTGRES_PASSWORD" exec psql -v ON_ERROR_STOP=1 --no-psqlrc -U "$POSTGRES_USER" -d postgres -f -'

if ! docker exec -i "$container" sh -c "$psql_cmd" < "$SQL_FILE"; then
    echo "ERROR: не удалось создать базы данных ($SQL_FILE)." >&2
    exit 1
fi

echo "Базы данных на месте."
