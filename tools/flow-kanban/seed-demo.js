/** CLI: npm run seed */
const path = require('path');
const os = require('os');
const { seedDemo } = require('./lib/demoData');

const specsDir = process.env.FLOW_SPECS_DIR || path.join(os.homedir(), '.flow', 'specs');
const result = seedDemo(specsDir);
console.log(`Done! Created ${result.created} specs (${result.total} total, ${result.projects} projects) in ${specsDir}`);
