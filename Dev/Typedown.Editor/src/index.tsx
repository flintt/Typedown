import React from 'react';
import ReactDOM from 'react-dom';
import App from './App';
import { installChangeOriginTracking } from 'services/changeOrigin';
import { installPageErrorReporting } from 'services/pageErrors';
import { markStartup } from 'services/startupMarks';
import { installTypographyGuides } from 'services/typographyGuides';

// The bundle downloaded, parsed and its modules run: the page's own start begins here (services/startupMarks).
markStartup('script run');

installPageErrorReporting();
installChangeOriginTracking();
installTypographyGuides();

ReactDOM.render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
  document.getElementById('root')
);
markStartup('react rendered');

