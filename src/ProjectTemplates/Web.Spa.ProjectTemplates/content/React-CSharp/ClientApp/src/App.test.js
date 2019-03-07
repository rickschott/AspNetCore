import React from 'react';
import ReactDOM from 'react-dom';
import { MemoryRouter } from 'react-router-dom';
import App from './App';

it('renders without crashing', async () => {
  const div = document.createElement('div');
  const promise = new Promise((resolve) => ReactDOM.render(
    <MemoryRouter>
      <App />
    </MemoryRouter>, div, resolve));
  await promise;
});
