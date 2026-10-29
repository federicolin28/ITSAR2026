using UnityEngine;

public class BankAccount : MonoBehaviour

    [SerializeField] int containedValue;
public int BankValue => containedValue;
public void AddmoneyToBank(int money)
{
    containedValue += money
}
  
