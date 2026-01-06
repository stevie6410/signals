import { Routes } from '@angular/router';
import { LayoutComponent } from './layout/layout.component';

export const routes: Routes = [
  {
    path: '',
    component: LayoutComponent,
    children: [
      {
        path: '',
        redirectTo: 'home',
        pathMatch: 'full'
      },
      {
        path: 'home',
        loadComponent: () => import('./features/home/home.component').then(m => m.HomeComponent)
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
        path: 'devices/network-map',
        loadComponent: () => import('./features/devices/zigbee-network-map/zigbee-network-map.component').then(m => m.ZigbeeNetworkMapComponent)
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
      },
      {
        path: 'zones/capabilities',
        loadComponent: () => import('./features/zones/zone-capabilities-page/zone-capabilities-page.component').then(m => m.ZoneCapabilitiesPageComponent)
      },
      {
        path: 'zones/:zoneId/capabilities',
        loadComponent: () => import('./features/zones/zone-capabilities-page/zone-capabilities-page.component').then(m => m.ZoneCapabilitiesPageComponent)
      },
      {
        path: 'automations',
        loadComponent: () => import('./features/automations/automations.component').then(m => m.AutomationsComponent)
      },
      {
        path: 'automations/new',
        loadComponent: () => import('./features/automations/automation-editor/automation-editor.component').then(m => m.AutomationEditorComponent)
      },
      {
        path: 'automations/:id',
        loadComponent: () => import('./features/automations/automation-editor/automation-editor.component').then(m => m.AutomationEditorComponent)
      },
      {
        path: 'workflows/new',
        loadComponent: () => import('./features/workflows/workflow-editor/workflow-editor.component').then(m => m.WorkflowEditorComponent)
      },
      {
        path: 'workflows/:id',
        loadComponent: () => import('./features/workflows/workflow-editor/workflow-editor.component').then(m => m.WorkflowEditorComponent)
      },
      {
        path: 'settings',
        loadComponent: () => import('./features/settings/capability-mappings/capability-mappings.component').then(m => m.CapabilityMappingsComponent)
      },
      {
        path: 'settings/capability-mappings',
        loadComponent: () => import('./features/settings/capability-mappings/capability-mappings.component').then(m => m.CapabilityMappingsComponent)
      }
    ]
  }
];
