$ErrorActionPreference = 'Stop'
$project = 'racehunter-playwright'

try {
    docker compose -p $project up -d --build --wait
    $env:RACEHUNTER_BASE_URL = 'http://127.0.0.1:8080'
    npm run test:real --prefix tests/RaceHunter.AcceptanceTests
}
finally {
    docker compose -p $project down -v --remove-orphans
}
