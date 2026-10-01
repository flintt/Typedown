import React from 'react';
import ReactDOM from 'react-dom';
import App from './App';
import { installChangeOriginTracking } from 'services/changeOrigin';

installChangeOriginTracking();

ReactDOM.render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
  document.getElementById('root')
);

