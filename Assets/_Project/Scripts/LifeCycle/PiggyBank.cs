using System.Collections;
using UnityEngine;


public class PiggyBank : MonoBehaviour
{
    [SerializeField] int maxCapacity = 10;
    [SerializeField] int currentValue;


    [SerializeField] BankAccount bankAccount;


    private void Awake()
    {
        currentValue = 0;
    }


    private void Start()
    {
        StartCoroutine(AddMoneyToPiggyBank());
    }
    IEnumerator AddMoneyToPiggyBank()
    {
        yield return new WaitForSeconds(1);
        currentValue++;
        {
            AddToAccount();
        }


        StartCoroutine(AddMoneyToPiggyBank());
    }


    void AddToAccount()
    {
        if (bankAccount)
        {
            bankAccount.AddMoneyToBank(currentValue);
            Debug.Log($"MONEY IN THE BANK: {bankAccount.BankValue}");
            currentValue = 0;
        }
    }
}