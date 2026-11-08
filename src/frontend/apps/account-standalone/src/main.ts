import { bootstrapApplication } from '@angular/platform-browser';
import { silenceConsoleInProduction } from '@oluso/ui-core';
import { appConfig } from './app/app.config';
import { App } from './app/app';

silenceConsoleInProduction();

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
