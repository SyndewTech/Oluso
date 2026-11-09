import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AdminUi } from './admin-ui';

describe('AdminUi', () => {
  let component: AdminUi;
  let fixture: ComponentFixture<AdminUi>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminUi],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminUi);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
