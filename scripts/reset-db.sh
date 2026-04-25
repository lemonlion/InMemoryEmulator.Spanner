#!/bin/bash
# Drop existing database (ignore errors)
curl -s -X DELETE 'http://localhost:19020/v1/projects/test-project/instances/test-instance/databases/test-db' 2>/dev/null
# Create fresh database
curl -s -X POST 'http://localhost:19020/v1/projects/test-project/instances/test-instance/databases' -H 'Content-Type: application/json' -d '{"createStatement":"CREATE DATABASE `test-db`"}'
echo ""
echo "Database reset complete."