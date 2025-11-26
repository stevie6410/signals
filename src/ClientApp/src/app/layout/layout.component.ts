import { Component, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { DrawerModule } from 'primeng/drawer';
import { MenuModule } from 'primeng/menu';
import { ToolbarModule } from 'primeng/toolbar';
import { MenuItem } from 'primeng/api';
import { ThemeService } from '../core/services/theme.service';

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    ButtonModule,
    DrawerModule,
    MenuModule,
    ToolbarModule
  ],
  templateUrl: './layout.component.html',
  styleUrl: './layout.component.scss'
})
export class LayoutComponent {
  themeService = inject(ThemeService);
  drawerVisible = false;

  menuItems: MenuItem[] = [
    {
      label: 'Dashboard',
      icon: 'pi pi-home',
      routerLink: '/dashboard'
    },
    {
      label: 'Signals',
      icon: 'pi pi-bolt',
      routerLink: '/signals'
    },
    {
      label: 'Readings',
      icon: 'pi pi-chart-line',
      routerLink: '/readings'
    },
    {
      label: 'Triggers',
      icon: 'pi pi-bell',
      routerLink: '/triggers'
    },
    {
      label: 'Devices',
      icon: 'pi pi-box',
      routerLink: '/devices'
    },
    {
      separator: true
    },
    {
      label: 'Settings',
      icon: 'pi pi-cog',
      routerLink: '/settings'
    }
  ];

  // Computed property for theme icon
  themeIcon = computed(() =>
    this.themeService.theme() === 'dark' ? 'pi pi-sun' : 'pi pi-moon'
  );

  toggleDrawer(): void {
    this.drawerVisible = !this.drawerVisible;
  }
}
