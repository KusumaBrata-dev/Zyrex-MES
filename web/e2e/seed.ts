import { execSync } from "node:child_process";

/**
 * Seeds the E2E dataset through `wsl -d Ubuntu -u root -- docker exec zyrex-pg
 * psql` (Playwright has no DB access; documented in web/README.md). Idempotent:
 * rows are deleted by the E2E- prefix first, then re-inserted, and the scanned
 * unit's transactions are wiped so the scan scenario always starts fresh.
 *
 * Password for e2e_op is "E2e!Pass123"; the stored value is an Argon2id hash
 * (format argon2id$<salt-b64>$<hash-b64>, m=19456 t=2 p=4) generated offline —
 * a hash is not a plaintext secret.
 */
const OP_HASH =
  "argon2id$YkHcx+sQ9Uq+yWuIzyb1gw==$Z6VaTbv2d0dUk46UCG8w2nK1mb7DhTs3pImOao4GA3k=";

function psql(sql: string): void {
  // Pipe the SQL over stdin (`docker exec -i`) so no shell quoting is needed.
  execSync(
    "wsl -d Ubuntu -u root -- docker exec -i zyrex-pg psql -U postgres -d zyrex_mes -v ON_ERROR_STOP=1",
    { input: sql, stdio: ["pipe", "inherit", "inherit"] },
  );
}

/** Single-quote a string for SQL and wrap it for Windows shell (double quotes). */
function sqlStr(value: string): string {
  return `'${value.replaceAll("'", "''")}'`;
}

export function seedE2eData(): void {
  psql(`
    -- Cascade-delete transactions first to avoid FK violations
    DELETE FROM "UnitTransactions" WHERE "UnitId" IN (SELECT "Id" FROM "Units" WHERE "SerialNumber" LIKE 'E2E-%');
    DELETE FROM "RoutingSteps" s USING "Routings" r, "Products" p
      WHERE s."RoutingId" = r."Id" AND r."ProductId" = p."Id" AND p."Sku" = 'E2E-SKU';
    DELETE FROM "Routings" r USING "Products" p
      WHERE r."ProductId" = p."Id" AND p."Sku" = 'E2E-SKU';
    DELETE FROM "Units" WHERE "SerialNumber" LIKE 'E2E-%';
    DELETE FROM "Stations" WHERE "Code" = 'ST-E2E';
    DELETE FROM "Lines" WHERE "Code" = 'E99';
    DELETE FROM "Products" WHERE "Sku" = 'E2E-SKU';

    INSERT INTO "users" ("Username", "PasswordHash", "FullName", "Role", "IsActive")
    VALUES ('e2e_op', ${sqlStr(OP_HASH)}, 'E2E Operator', 'Operator', true)
    ON CONFLICT ("Username") DO UPDATE SET "PasswordHash" = EXCLUDED."PasswordHash", "IsActive" = true;

    INSERT INTO "Lines" ("Code", "Name", "IsActive")
    VALUES ('E99', 'E2E Line', true);

    INSERT INTO "Stations" ("LineId", "Code", "Name", "IsEnabled")
    SELECT l."Id", 'ST-E2E', 'E2E Station', true FROM "Lines" l WHERE l."Code" = 'E99';

    INSERT INTO "Products" ("Sku", "Name", "IsActive")
    VALUES ('E2E-SKU', 'E2E Product', true);

    INSERT INTO "Routings" ("ProductId", "Name", "IsActive")
    SELECT p."Id", 'RT-E2E', true FROM "Products" p WHERE p."Sku" = 'E2E-SKU';

    INSERT INTO "RoutingSteps" ("RoutingId", "Sequence", "StationId", "RequireLabel")
    SELECT r."Id", 10, s."Id", false
    FROM "Routings" r
    JOIN "Products" p ON p."Id" = r."ProductId"
    JOIN "Stations" s ON s."Code" = 'ST-E2E'
    WHERE p."Sku" = 'E2E-SKU' AND r."Name" = 'RT-E2E';

    INSERT INTO "Units" ("SerialNumber", "ProductId", "Status", "CreatedAtUtc")
    SELECT 'E2E-SN-0001', p."Id", 'Created', now()
    FROM "Products" p WHERE p."Sku" = 'E2E-SKU';
  `);
}

/** Supervisor login for dashboard E2E (ack-capable role). Same offline hash
 *  as e2e_op → password is also "E2e!Pass123". */
export function seedE2eSupervisor(): void {
  psql(`
    INSERT INTO "users" ("Username", "PasswordHash", "FullName", "Role", "IsActive")
    VALUES ('e2e_sup', ${sqlStr(OP_HASH)}, 'E2E Supervisor', 'Supervisor', true)
    ON CONFLICT ("Username") DO UPDATE SET "PasswordHash" = EXCLUDED."PasswordHash", "Role" = 'Supervisor', "IsActive" = true;
  `);
}

/** Id of the seeded E2E station (for the /scan?stationId= URL pattern). */
export function e2eStationId(): number {
  const out = execSync(
    'wsl -d Ubuntu -u root -- docker exec zyrex-pg psql -U postgres -d zyrex_mes -t -A -c "SELECT s.\\"Id\\" FROM \\"Stations\\" s WHERE s.\\"Code\\" = \'ST-E2E\'"',
  )
    .toString()
    .trim();
  const id = Number.parseInt(out, 10);
  if (!Number.isInteger(id) || id <= 0) throw new Error(`unexpected station id: ${out}`);
  return id;
}