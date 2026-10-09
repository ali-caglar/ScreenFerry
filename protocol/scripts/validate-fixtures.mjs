// Validates protocol/fixtures/<schema>/*.json against protocol/schemas/<schema>.schema.json.
import { readdirSync, readFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const schemasDir = join(root, "schemas");
const fixturesDir = join(root, "fixtures");

const ajv = new Ajv2020({ allErrors: true, strict: true });
addFormats(ajv);

const schemaFiles = readdirSync(schemasDir).filter((f) => f.endsWith(".schema.json"));
for (const file of schemaFiles) {
  ajv.addSchema(JSON.parse(readFileSync(join(schemasDir, file), "utf8")), file.replace(".schema.json", ""));
}

let failures = 0;
let checked = 0;
for (const dir of readdirSync(fixturesDir, { withFileTypes: true })) {
  if (!dir.isDirectory()) continue;
  const validate = ajv.getSchema(dir.name);
  if (!validate) {
    console.error(`✗ fixtures/${dir.name}/: no schemas/${dir.name}.schema.json`);
    failures++;
    continue;
  }
  for (const file of readdirSync(join(fixturesDir, dir.name)).filter((f) => f.endsWith(".json"))) {
    const data = JSON.parse(readFileSync(join(fixturesDir, dir.name, file), "utf8"));
    checked++;
    if (validate(data)) {
      console.log(`✓ fixtures/${dir.name}/${file}`);
    } else {
      failures++;
      console.error(`✗ fixtures/${dir.name}/${file}`);
      for (const err of validate.errors) console.error(`    ${err.instancePath || "/"} ${err.message}`);
    }
  }
}

if (checked === 0) {
  console.error("No fixtures found.");
  process.exit(1);
}
console.log(`${checked - failures}/${checked} fixtures valid against ${schemaFiles.length} schema(s).`);
process.exit(failures ? 1 : 0);
