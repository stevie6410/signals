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
      },
      {
        path: 'devices/:deviceId',
        loadComponent: () => import('./features/devices/device-detail/device-detail.component').then(m => m.DeviceDetailComponent)
      },
      {
        path: 'zones',
        loadComponent: () => import('./features/zones/zones.component').then(m => m.ZonesComponent)
      },
      {
        path: 'zones/manage',
        loadComponent: () => import('./features/zones/zone-manager/zone-manager.component').then(m => m.ZoneManagerComponent)
      }
    ]
  }
];
