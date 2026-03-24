#!/bin/bash
# Creates the pretix and pretalx databases on first PostgreSQL start.
# This script is mounted into /docker-entrypoint-initdb.d/ and runs automatically.

set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-EOSQL
    CREATE DATABASE pretix;
    CREATE DATABASE pretalx;
EOSQL
