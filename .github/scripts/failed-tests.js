// Prints each failed test of a .trx file as a GitHub annotation, so the reason is visible without opening the full log.
const fs = require('fs');

const file = process.argv[2] || 'results/backend.trx';
if (!fs.existsSync(file)) {
  console.log('::error title=No test results::' + file + ' was not written (the build or the test run stopped early).');
  process.exit(0);
}

const xml = fs.readFileSync(file, 'utf8');
const failed = [...xml.matchAll(/<UnitTestResult\b[^>]*>[\s\S]*?<\/UnitTestResult>/g)].map(m => m[0]).filter(r => /outcome="Failed"/.test(r));

for (const r of failed.slice(0, 15)) {
  const name = (r.match(/testName="([^"]*)"/) || [, '?'])[1];
  const message = (r.match(/<Message>([\s\S]*?)<\/Message>/) || [, ''])[1].replace(/\r?\n/g, ' | ').slice(0, 900);
  console.log('::error title=Failed test::' + name + ' => ' + message);
}
console.log('failed tests: ' + failed.length);
