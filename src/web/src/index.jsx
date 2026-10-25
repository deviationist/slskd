import 'semantic-ui-less/semantic.less';
import App from './components/App';
import { registerServiceWorker } from './lib/serviceWorker';
import React from 'react';
import ReactDOM from 'react-dom';
import { BrowserRouter as Router } from 'react-router-dom';

ReactDOM.render(
  <Router>
    <App />
  </Router>,
  document.querySelector('#root'),
);

registerServiceWorker();
