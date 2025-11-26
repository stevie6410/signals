import { Routes } from '@angular/router';
import { LayoutComponent } from './layout/layout.component';

export const routes: Routes = [
  {
    path: '',
    component: LayoutComponent,
    children: [
      {
        path: '',
        redirectTo: 'dashboard',
        pathMatch: 'full'
      },
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent)
      },
      {
        path: 'signals',
        loadComponent: () => import('./features/signals/signals.component').then(m => m.SignalsComponent)
      },
      {
        path: 'readings',
        loadComponent: () => import('./features/readings/readings.component').then(m => m.ReadingsComponent)
      },
      {
        path: 'triggers',
        loadComponent: () => import('./features/triggers/triggers.component').then(m => m.TriggersComponent)
      },
      {
        path: 'devices',
        loadComponent: () => import('./features/devices/devices.component').then(m => m.DevicesComponent)
      }
    ]
  }
];
