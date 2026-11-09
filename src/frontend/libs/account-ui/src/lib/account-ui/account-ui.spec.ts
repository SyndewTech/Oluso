import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AccountUi } from './account-ui';

describe('AccountUi', () => {
  let component: AccountUi;
  let fixture: ComponentFixture<AccountUi>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AccountUi],
    }).compileComponents();

    fixture = TestBed.createComponent(AccountUi);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
