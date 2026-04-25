#!/bin/bash
curl -s -X POST 'http://localhost:19020/v1/projects/test-project/instances/test-instance/databases' -H 'Content-Type: application/json' -d '{"createStatement": "CREATE DATABASE test_db"}'