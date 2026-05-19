const path = require('path');

module.exports = {
  '*.{js,jsx,mjs,ts,tsx,css,scss,html,json,md}': (absolutePaths) => {
    const cwd = process.cwd();
    const relativePaths = absolutePaths
      .map((file) => path.relative(cwd, file))
      .join(' ');
    return [`prettier --write ${relativePaths}`];
  },
};
