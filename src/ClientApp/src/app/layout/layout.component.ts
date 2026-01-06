import { Component, inject, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router, NavigationEnd } from '@angular/router';
import { filter } from 'rxjs/operators';
import { SignalRService } from '../core/services/signalr.service';

interface NavItem {
  label: string;
  icon: string;
  route: string;
  badge?: () => number;
}

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './layout.component.html',
  styleUrl: './layout.component.scss'
})
export class LayoutComponent implements OnInit {
  signalR = inject(SignalRService);
  router = inject(Router);

  drawerVisible = false;
  currentRoute = signal('');

  navItems: NavItem[] = [
    { label: 'Home', icon: '🏠', route: '/home' },
    { label: 'Live Monitor', icon: '⚡', route: '/signals', badge: () => this.signalR.signalCount() },
    { label: 'Readings', icon: '📊', route: '/readings' },
    { label: 'Triggers', icon: '🔔', route: '/triggers' },
    { label: 'Devices', icon: '📡', route: '/devices' },
    { label: 'Zones', icon: '🏢', route: '/zones' },
    { label: 'Automations', icon: '🤖', route: '/automations' },
    { label: 'Analytics', icon: '📈', route: '/dashboard' },
    { label: 'Settings', icon: '⚙️', route: '/settings' },
  ];

  // Computed connection status
  connectionStatus = computed(() => {
    switch (this.signalR.connectionState()) {
      case 'connected': return { text: 'Live', class: 'status-live' };
      case 'connecting': return { text: 'Connecting...', class: 'status-connecting' };
      case 'reconnecting': return { text: 'Reconnecting...', class: 'status-reconnecting' };
      default: return { text: 'Offline', class: 'status-offline' };
    }
  });

  ngOnInit(): void {
    // Connect to SignalR
    this.signalR.connect();

    // Track current route for active state
    this.router.events.pipe(
      filter(event => event instanceof NavigationEnd)
    ).subscribe((event: any) => {
      this.currentRoute.set(event.urlAfterRedirects);
    });

    this.currentRoute.set(this.router.url);
  }

  isActive(route: string): boolean {
    return this.currentRoute().startsWith(route);
  }

  toggleDrawer(): void {
    this.drawerVisible = !this.drawerVisible;
  }

  closeDrawer(): void {
    this.drawerVisible = false;
  }
}
