using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class VariablesTest : MonoBehaviour
{
    [SerializeField] int shopIndex = 0;
    [SerializeField] float shopRevenues = 50.0f;

    [SerializeField] bool shopOpen = false;

    public enum ShopState { OPENING = 0, SELLING, RESTOCKING,CLOSING }
    [SerializeField] ShopState shopStateBase = ShopState.OPENING;

    [SerializeField] ShopState shopStateAdvanced = ShopState.RESTOCKING;

    [SerializeField] string ShopBase = "shop";

    [SerializeField] int[] productArray = new int[5];

    [SerializeField] List<int> productList = new List<int>();
    //keys=nome prodotto, float = prezzo prodott;
    Dictionary<string, float> productionDictionary = new Dictionary<string, float>();

    HashSet<Product> productionHashSet = new HashSet<Product>();

    [SerializeField] List<Product> productsList = new List<Product>();

}
[Serializable]
public class Product
{
    public string Name;
    public float Price;

}