import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';

interface Product {
  id: string;
  name: string;
  sku: string;
  price: number;
  unitOfMeasure: string;
  categoryId: string;
  imageUrl?: string;
}

interface Category {
  id: string;
  name: string;
}

interface Store {
  id: string;
  name: string;
  address: string;
  serviceRadiusKm: number;
}

interface ApiResult<T> {
  data: T;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrl: './App.css',
})
export class AppComponent implements OnInit {
  private readonly http = inject(HttpClient);
  readonly api = 'http://localhost:5067/api';
  readonly fallbackProducts: Product[] = [
    { id: '1', name: 'Tomato', sku: 'VEG-TOM', price: 45, unitOfMeasure: '1 kg', categoryId: 'vegetables', imageUrl: 'https://images.unsplash.com/photo-1546094096-0df4bcaaa337?w=640' },
    { id: '2', name: 'Potato', sku: 'VEG-POT', price: 38, unitOfMeasure: '1 kg', categoryId: 'vegetables', imageUrl: 'https://images.unsplash.com/photo-1518977676601-b53f82aba655?w=640' },
    { id: '3', name: 'Farm Milk', sku: 'DAI-MILK', price: 34, unitOfMeasure: '500 ml', categoryId: 'dairy', imageUrl: 'https://images.unsplash.com/photo-1550583724-b2692b85b150?w=640' },
    { id: '4', name: 'Royal Gala Apples', sku: 'FRT-APL', price: 149, unitOfMeasure: '1 kg', categoryId: 'fruits', imageUrl: 'https://images.unsplash.com/photo-1560806887-1e4cd0b6cbd6?w=640' },
  ];
  products = [...this.fallbackProducts];
  categories: Category[] = [];
  stores: Store[] = [];
  query = '';
  selectedCategory = 'all';
  online = false;

  get visibleProducts(): Product[] {
    const normalizedQuery = this.query.toLowerCase();
    return this.products.filter((product) => product.name.toLowerCase().includes(normalizedQuery) && (this.selectedCategory === 'all' || product.categoryId === this.selectedCategory));
  }

  ngOnInit(): void {
    this.loadDashboardData();
  }

  private loadDashboardData(): void {
    this.http.get<ApiResult<Product[]>>(`${this.api}/products`).subscribe({
      next: (productResult) => {
        this.products = productResult.data;
        this.online = true;
        this.loadSupportingData();
      },
      error: () => {
        this.online = false;
      },
    });
  }

  private loadSupportingData(): void {
    this.http.get<ApiResult<Category[]>>(`${this.api}/categories`).subscribe({ next: (result) => this.categories = result.data });
    this.http.get<ApiResult<Store[]>>(`${this.api}/stores`).subscribe({ next: (result) => this.stores = result.data });
  }
}
